module CosmoStore.CosmosDb.EventStore

open System
open System.Threading.Tasks
open Newtonsoft.Json.Linq
open Microsoft.Azure.Documents
open Microsoft.Azure.Documents.Client
open FSharp.Control.Tasks.V2
open CosmoStore
open Microsoft.Azure.Documents.Client

let private collectionName = "Events"
let private partitionKey = "streamId"
let private appendEventProcName = "AppendEvent" 


let private createDatabase dbName (client:DocumentClient) =
    task {
        let db = new Database (Id = dbName)
        let! _ = client.CreateDatabaseIfNotExistsAsync(db)
        return ()
    }
 
let private createCollection dbName (capacity:Capacity) (throughput:int) (client:DocumentClient) =
    let collection = DocumentCollection( Id = collectionName)
    
    // partition key
    if capacity.UsePartitionKey then
        collection.PartitionKey.Paths.Add(sprintf "/%s" partitionKey)
    
    // unique keys
    let paths = new System.Collections.ObjectModel.Collection<string>()
    paths.Add("/streamId")
    paths.Add("/position")
    let keys = new System.Collections.ObjectModel.Collection<UniqueKey>()
    keys.Add(UniqueKey( Paths = paths))
    collection.UniqueKeyPolicy <- new UniqueKeyPolicy(UniqueKeys = keys)
    
    // throughput
    let throughput = throughput |> capacity.CorrectThroughput
    let ro = new RequestOptions()
    ro.OfferThroughput <- Nullable<int>(throughput)

    let dbUri = UriFactory.CreateDatabaseUri dbName
    task {
        let! _ = client.CreateDocumentCollectionIfNotExistsAsync(dbUri, collection, ro)
        return ()
    }

let private createStoreProcedures dbName (client:DocumentClient) =
    let collUri = UriFactory.CreateDocumentCollectionUri(dbName, collectionName)
    task {
        let! _ = client.UpsertStoredProcedureAsync(collUri, StoredProcedure(Id = appendEventProcName, Body = CosmoStore.CosmosDb.StoredProcedures.appendEvent))
        return ()
    }

/// Append events to stream
let private appendEvents getOpts (client:DocumentClient) (storedProcUri:Uri) (streamId:string) (expectedPosition:ExpectedPosition) (events:EventWrite list) = 
    let toPositionAndDate (doc:Document) =
        doc.GetPropertyValue<int64>("position"), doc.GetPropertyValue<DateTime>("created")
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

/// Append event to stream
let private appendEvent getOpts (client:DocumentClient) (storedProcUri:Uri) (streamId:string) (expectedPosition:ExpectedPosition) (event:EventWrite) = 
    task {
        let! events = event |> List.singleton |> appendEvents getOpts client storedProcUri streamId expectedPosition
        return events.Head
    }
    
let private getRequestOptions usePartitionKey streamId =
    if usePartitionKey then RequestOptions(PartitionKey = PartitionKey(streamId))
    else RequestOptions()    

let getEventStore (configuration:Configuration) = 
    let client = new DocumentClient(configuration.ServiceEndpoint, configuration.AuthKey)
    let appendEventProcUri = UriFactory.CreateStoredProcedureUri(configuration.DatabaseName, collectionName, appendEventProcName)

    task {
        do! createDatabase configuration.DatabaseName client
        do! createCollection configuration.DatabaseName configuration.Capacity configuration.Throughput client
        do! createStoreProcedures configuration.DatabaseName client
    } |> Async.AwaitTask |> Async.RunSynchronously
    
    let dummy = {
        Id = Guid.Empty
        CorrelationId = Guid.Empty
        StreamId = ""
        Position = 0L
        Name = ""
        Data = JValue("") :> JToken
        Metadata = None
        CreatedUtc = DateTime.UtcNow
    }
    
    let getOpts = getRequestOptions configuration.Capacity.UsePartitionKey
    {
        AppendEvent = appendEvent getOpts client appendEventProcUri
        AppendEvents = appendEvents getOpts client appendEventProcUri
        GetEvent = fun _ _ -> task { return dummy }//: string -> int64 -> Task<EventRead>
        GetEvents = fun _ _ -> task { return [dummy]}// string -> EventsReadRange -> Task<EventRead list>
        GetStreams = fun _ -> task { return ["dummy"]}// StreamsReadFilter -> Task<string list>
    }
