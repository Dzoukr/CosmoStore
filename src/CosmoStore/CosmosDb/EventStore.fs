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

type Capacity =
    | Fixed
    | Unlimited
    with
        member x.ThroughputLimits =
            match x with
            | Fixed -> 400, 10000
            | Unlimited -> 1000, 100000
        
        member x.CorrectThroughput (i:int) =
            let throughput = Math.Round((decimal i) / 100m, 0) * 100m |> int
            let min,max = x.ThroughputLimits
            if throughput > max then max
            else if throughput < min then min
            else throughput
        
        member x.UsePartitionKey =
            match x with
            | Fixed -> false
            | Unlimited -> true

type Configuration = {
    DatabaseName : string
    ServiceEndpoint : Uri
    AuthKey : string
    Capacity: Capacity
    Throughput: int
}
with
    static member CreateDefault serviceEndpoint authKey = {
        DatabaseName = "EventStore"
        ServiceEndpoint = serviceEndpoint
        AuthKey = authKey
        Capacity = Fixed
        Throughput = Fixed.ThroughputLimits |> fst
    }

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
        let! _ = client.UpsertStoredProcedureAsync(collUri, StoredProcedure(Id = "AppendEvent", Body = CosmoStore.CosmosDb.StoredProcedures.appendEvent))
        return ()
    }

let getEventStore (configuration:Configuration) = 
    let client = new DocumentClient(configuration.ServiceEndpoint, configuration.AuthKey)
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
    
    {
        AppendEvent = fun _ _ _ -> task { return dummy }//: string -> ExpectedPosition -> EventWrite -> Task<EventRead>
        AppendEvents = fun _ _ _ -> task { return [dummy] } // string -> ExpectedPosition -> EventWrite list -> Task<EventRead list>
        GetEvent = fun _ _ -> task { return dummy }//: string -> int64 -> Task<EventRead>
        GetEvents = fun _ _ -> task { return [dummy]}// string -> EventsReadRange -> Task<EventRead list>
        GetStreams = fun _ -> task { return ["dummy"]}// StreamsReadFilter -> Task<string list>
    }
