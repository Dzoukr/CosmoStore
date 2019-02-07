module CosmoStore.CosmosDb.EventStore

open System
open Microsoft.Azure.Documents
open Microsoft.Azure.Documents.Client
open FSharp.Control.Tasks.V2
open CosmoStore
open System.Reflection
open System.IO
open CosmoStore.CosmosDb
open System.Reactive.Linq
open System.Reactive.Concurrency

let private collectionName = "Events"
let private partitionKey = "streamId"
let private appendEventProcName = "AppendEvents"

let private createDatabase dbName (client:DocumentClient) =
    task {
        let db = new Database (Id = dbName)
        let! _ = client.CreateDatabaseIfNotExistsAsync(db)
        return ()
    }
 
let private createCollection (dbUri:Uri) (throughput:int) (client:DocumentClient) =
    let collection = DocumentCollection( Id = collectionName)
    
    // always use partition key
    collection.PartitionKey.Paths.Add(sprintf "/%s" partitionKey)
    
    // unique keys
    let streamPath = new System.Collections.ObjectModel.Collection<string>()
    streamPath.Add("/streamId")
    streamPath.Add("/position")
    let keys = new System.Collections.ObjectModel.Collection<UniqueKey>()
    keys.Add(UniqueKey( Paths = streamPath))
    collection.UniqueKeyPolicy <- new UniqueKeyPolicy(UniqueKeys = keys)
    
    // throughput
    let throughput = throughput |> Throughput.correct
    let ro = new RequestOptions()
    ro.OfferThroughput <- Nullable<int>(throughput)

    task {
        let! _ = client.CreateDocumentCollectionIfNotExistsAsync(dbUri, collection, ro)
        return ()
    }

let private getStoredProcedure name =
    task {
        let ass = typeof<CosmoStore.CosmosDb.Configuration>.GetTypeInfo().Assembly
        use stream = ass.GetManifestResourceStream(sprintf "CosmoStore.CosmosDb.StoredProcedures.%s" name)
        use reader = new StreamReader(stream)
        return! reader.ReadToEndAsync()
    }

let private createStoreProcedures (collUri:Uri) (procUri:Uri) (client:DocumentClient) =
    task {
        try
            let! _ = client.DeleteStoredProcedureAsync(procUri)
            ()
        with _ -> ()
        let! storedProc = getStoredProcedure "AppendEvents.js"
        let! _ = client.CreateStoredProcedureAsync(collUri, StoredProcedure(Id = appendEventProcName, Body = storedProc))
        return ()
    }

let private appendEvents getOpts (client:DocumentClient) (storedProcUri:Uri) (streamId:string) (expectedPosition:ExpectedPosition) (events:EventWrite list) = 
    let toPositionAndDate (doc:Document) = doc.GetPropertyValue<int64>("position"), doc.GetPropertyValue<DateTime>("created")
    task {
        let jEvents = events |> List.map Serialization.objectToJToken
        let jPosition = expectedPosition |> Serialization.expectedPositionToJObject
        let opts = streamId |> getOpts
        let pars = [|streamId :> obj; jEvents :> obj; jPosition :> obj|]
        let! resp = client.ExecuteStoredProcedureAsync<List<Document>>(storedProcUri, opts, pars)
        return resp.Response 
        |> List.map toPositionAndDate 
        |> List.zip events
        |> List.map (fun (evn,(pos,created)) -> Conversion.eventWriteToEventRead streamId pos created evn)
    }

let private streamsReadToQuery = function
    | AllStreams -> "", ("@_", null)
    | StarsWith w -> "AND STARTSWITH(e.streamId, @streamId)", ("@streamId", w :> obj)
    | EndsWith w -> "AND ENDSWITH(e.streamId, @streamId)", ("@streamId", w :> obj)
    | Contains t -> "AND CONTAINS(e.streamId, @streamId)", ("@streamId", t :> obj)

let private createQuery q (pars:(string * obj) list) =
    let ps = pars |> List.map (fun (x,y) -> SqlParameter(x, y)) |> SqlParameterCollection
    SqlQuerySpec(q, ps)

let private runQuery<'a> (client:DocumentClient) (collectionUri:Uri) (q:SqlQuerySpec) =
    let opts = new FeedOptions()
    opts.EnableCrossPartitionQuery <- true
    opts.EnableScanInQuery <- Nullable<bool>(true)
    client.CreateDocumentQuery<'a>(collectionUri, q, opts) 

let private getStreams (client:DocumentClient) (collectionUri:Uri) (streamsRead:StreamsReadFilter) =
    task {
        let queryAdd,param = streamsRead |> streamsReadToQuery
        return createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.type = 'Stream' %s" collectionName queryAdd) [param]
        |> runQuery<Document> client collectionUri
        |> Seq.toList
        |> List.map Conversion.documentToStream
        |> List.sortBy (fun x -> x.Id)
    }

let private streamEventsReadToQuery = function
    | AllEvents -> ""
    | FromPosition pos -> sprintf "AND e.position >= %i" pos
    | ToPosition pos -> sprintf "AND e.position <= %i" pos
    | PositionRange(st,en) -> sprintf "AND e.position >= %i AND e.position <= %i" st en

let private getEvents (client:DocumentClient) (collectionUri:Uri) streamId (eventsRead:EventsReadRange) =
    task {
        return createQuery 
            (sprintf "SELECT * FROM %s e WHERE e.streamId = @streamId AND e.type = 'Event' %s ORDER BY e.position ASC" collectionName (streamEventsReadToQuery eventsRead))
            ["@streamId", streamId :> obj]
        |> runQuery<Document> client collectionUri
        |> Seq.toList
        |> List.map Conversion.documentToEventRead
    }

let private getEvent (client:DocumentClient) (collectionUri:Uri) streamId position =
    task {
        let filter = EventsReadRange.PositionRange(position, position)
        let! events = getEvents client collectionUri streamId filter
        return events.Head
    }

let private getStream (client:DocumentClient) (collectionUri:Uri) streamId =
    task {
        return createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.type = 'Stream' AND e.streamId = @streamId" collectionName) [("@streamId", streamId :> obj)]
        |> runQuery<Document> client collectionUri
        |> Seq.toList
        |> List.map Conversion.documentToStream
        |> List.head
    }

let private getRequestOptions streamId = RequestOptions(PartitionKey = PartitionKey(streamId))

let getEventStore (configuration:Configuration) = 
    let client = new DocumentClient(configuration.ServiceEndpoint, configuration.AuthKey)
    let dbUri = UriFactory.CreateDatabaseUri(configuration.DatabaseName)
    let eventsCollectionUri = UriFactory.CreateDocumentCollectionUri(configuration.DatabaseName, collectionName)
    let appendEventProcUri = UriFactory.CreateStoredProcedureUri(configuration.DatabaseName, collectionName, appendEventProcName)
    let eventAppended = Event<EventRead>()

    task {
        do! createDatabase configuration.DatabaseName client
        do! createCollection dbUri configuration.Throughput client
        do! createStoreProcedures eventsCollectionUri appendEventProcUri client
    } |> Async.AwaitTask |> Async.RunSynchronously
    
    {
        AppendEvent = fun stream pos event -> task {
            let! events = appendEvents getRequestOptions client appendEventProcUri stream pos [event]
            events |> List.iter eventAppended.Trigger
            return events |> List.head
        }
                        
        AppendEvents = fun stream pos events -> task {
            if events |> List.isEmpty then return []
            else 
                let! events = appendEvents getRequestOptions client appendEventProcUri stream pos events
                events |> List.iter eventAppended.Trigger
                return events
        } 

        GetEvent = getEvent client eventsCollectionUri
        GetEvents = getEvents client eventsCollectionUri
        GetStreams = getStreams client eventsCollectionUri
        GetStream = getStream client eventsCollectionUri
        EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
    }