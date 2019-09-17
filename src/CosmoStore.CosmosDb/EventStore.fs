module CosmoStore.CosmosDb.EventStore

open System
open Microsoft.Azure.Cosmos
open FSharp.Control.Tasks.V2
open CosmoStore
open System.Reflection
open System.IO
open CosmoStore.CosmosDb
open System.Reactive.Linq
open System.Reactive.Concurrency
open Newtonsoft.Json.Linq
open Microsoft.Azure.Cosmos.Scripts

let private partitionKey = "streamId"
let private appendEventProcName = "AppendEvents"

let private createDatabase dbName (client:CosmosClient) =
    task {
        let! db = client.CreateDatabaseIfNotExistsAsync(dbName)
        return db.Database
    }
 
let private createContainer (db:Database) containerName throughput =
    let props = ContainerProperties()
    props.Id <- containerName

    // always use partition key
    props.PartitionKeyPath <- sprintf "/%s" partitionKey
    
    // unique keys
    let uk = UniqueKey ()
    uk.Paths.Add "/streamId"
    uk.Paths.Add "/position"

    let ukPolicy = UniqueKeyPolicy()
    ukPolicy.UniqueKeys.Add(uk)

    // throughput
    let throughput = throughput |> Throughput.correct |> Nullable<int>

    task {
        let! cont = db.CreateContainerIfNotExistsAsync(props,throughput)
        return cont.Container
    }

let private getStoredProcedure name =
    task {
        let ass = typeof<CosmoStore.CosmosDb.Configuration>.GetTypeInfo().Assembly
        use stream = ass.GetManifestResourceStream(sprintf "CosmoStore.CosmosDb.StoredProcedures.%s" name)
        use reader = new StreamReader(stream)
        return! reader.ReadToEndAsync()
    }

let private createStoreProcedures (container:Container) =
    task {
        try
            let! _ = container.Scripts.DeleteStoredProcedureAsync(appendEventProcName)
            ()
        with _ -> ()
        let! storedProcTemplate = getStoredProcedure "AppendEvents.js"
        let storedProc = storedProcTemplate.Replace("%%CONTAINER_NAME%%", container.Id)
        let! _ = container.Scripts.CreateStoredProcedureAsync(StoredProcedureProperties(Id = appendEventProcName, Body = storedProc))
        return ()
    }

type PositionAndCreated = { Position : int64; Created : DateTime }

let private appendEvents (container:Container) (streamId:StreamId) (expectedPosition:ExpectedPosition<int64>) (events:EventWrite<_> list) = 
    task {
        let jEvents = events |> List.map Serialization.objectToJToken
        let jPosition = expectedPosition |> Serialization.expectedPositionToJObject
        let pk = PartitionKey(streamId)
        let pars = [|streamId :> obj; jEvents :> obj; jPosition :> obj|]
        let! resp = container.Scripts.ExecuteStoredProcedureAsync<List<PositionAndCreated>> (appendEventProcName, pk, pars)
        return resp.Resource 
            |> List.zip events
            |> List.map (fun (evn,p) -> Conversion.eventWriteToEventRead streamId p.Position p.Created evn)
    }

let private streamsReadToQuery = function
    | AllStreams -> "", ("@_", null)
    | StartsWith w -> "AND STARTSWITH(e.streamId, @streamId)", ("@streamId", w :> obj)
    | EndsWith w -> "AND ENDSWITH(e.streamId, @streamId)", ("@streamId", w :> obj)
    | Contains t -> "AND CONTAINS(e.streamId, @streamId)", ("@streamId", t :> obj)

let private createQuery q (pars:(string * obj) list) =
    let foldFn (acc:QueryDefinition) (item:string * obj) = acc.WithParameter item
    pars |> List.fold foldFn (QueryDefinition q)

let private toEventRead (doc:JObject) = 
    { 
        Id = doc.Value<string> "id" |> Guid
        CorrelationId = doc.Value<string>("correlationId") |> function | null -> None | x -> Some (Guid x)
        CausationId = doc.Value<string>("causationId") |> function | null -> None | x -> Some (Guid x)
        StreamId = doc.Value<string>("streamId")
        Position = doc.Value<int64>("position")
        Name = doc.Value<string>("name")
        Data = doc.Value<JToken>("data")
        Metadata = doc.Value<JToken>("metadata") |> function | null -> None | x -> Some x
        CreatedUtc = doc.Value<DateTime>("createdUtc")
    }

let private toStream (x:JObject) = {
    Id = x.Value<string>("streamId")
    LastUpdatedUtc = x.Value<DateTime>("lastUpdatedUtc")
    LastPosition = x.Value<int64>("lastPosition")
}

let private runQuery<'a> mapFn (container:Container) (q:QueryDefinition) =
    let opts = QueryRequestOptions()
    opts.EnableScanInQuery <- Nullable<bool>(true)
    let iterator = container.GetItemQueryIterator<JObject>(q, null, opts)
    let items = ResizeArray<'a>()
    task {
        while iterator.HasMoreResults do
            let! values = iterator.ReadNextAsync()
            values.Resource |> Seq.toList |> List.map mapFn |> items.AddRange
        return items |> Seq.toList
    }

let private getStreams (container:Container) containerName (streamsRead:StreamsReadFilter) =
    task {
        let queryAdd,param = streamsRead |> streamsReadToQuery
        let! res = 
            createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.type = 'Stream' %s" containerName queryAdd) [param]
            |> runQuery<CosmoStore.Stream<_>> toStream container
        return res |> List.sortBy (fun x -> x.Id)
    }

let private streamEventsReadToQuery = function
    | AllEvents -> ""
    | FromPosition pos -> sprintf "AND e.position >= %i" pos
    | ToPosition pos -> sprintf "AND e.position <= %i" pos
    | PositionRange(st,en) -> sprintf "AND e.position >= %i AND e.position <= %i" st en

let private getEvents (container:Container) containerName streamId (eventsRead:EventsReadRange<_>) =
    task {
        return! 
            createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.streamId = @streamId AND e.type = 'Event' %s ORDER BY e.position ASC" containerName (streamEventsReadToQuery eventsRead))
                ["@streamId", streamId :> obj]
            |> runQuery<EventRead<_,_>> toEventRead container
    }

let private getEvent (container:Container) containerName streamId position =
    task {
        let filter = EventsReadRange.PositionRange(position, position)
        let! events = getEvents container containerName streamId filter
        return events.Head
    }

let private getEventsByCorrelationId (container:Container) containerName (corrId:Guid) =
    task {
        return! createQuery 
            (sprintf "SELECT * FROM %s e WHERE e.correlationId = @corrId AND e.type = 'Event' ORDER BY e.createdUtc ASC" containerName)
            ["@corrId", corrId :> obj]
        |> runQuery<EventRead<_,_>> toEventRead container
    }

let private getStream (container:Container) containerName streamId =
    task {
        let! streams = 
            createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.type = 'Stream' AND e.streamId = @streamId" containerName) [("@streamId", streamId :> obj)]
            |> runQuery<CosmoStore.Stream<_>> toStream container
        return streams |> List.head
    }

let getEventStore (configuration:Configuration) = 
    let client = new CosmosClient(configuration.ConnectionString)
    let eventAppended = Event<EventRead<JToken,int64>>()

    if configuration.InitializeContainer then
        task {
            let! db = createDatabase configuration.DatabaseName client
            let! cont = createContainer db configuration.ContainerName configuration.Throughput
            do! createStoreProcedures cont
        } |> Async.AwaitTask |> Async.RunSynchronously
    
    let container = client.GetContainer(configuration.DatabaseName, configuration.ContainerName)

    {
        AppendEvent = fun stream pos event -> task {
            let! events = appendEvents container stream pos [event]
            events |> List.iter eventAppended.Trigger
            return events |> List.head
        }
                        
        AppendEvents = fun stream pos events -> task {
            if events |> List.isEmpty then return []
            else 
                let! events = appendEvents container stream pos events
                events |> List.iter eventAppended.Trigger
                return events
        } 

        GetEvent = getEvent container configuration.ContainerName
        GetEvents = getEvents container configuration.ContainerName
        GetEventsByCorrelationId = getEventsByCorrelationId container configuration.ContainerName
        GetStreams = getStreams container configuration.ContainerName
        GetStream = getStream container configuration.ContainerName
        EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
    }