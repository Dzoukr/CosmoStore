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


let private createDatabaseIfNotExists dbName (client:CosmosClient) =
    task {
        let! db = client.CreateDatabaseIfNotExistsAsync(dbName)
        return db.Database
    }
 
let private createContainerIfNotExists (db:Database) containerName throughput =
    let props = ContainerProperties()
    props.Id <- containerName

    // always use partition key
    props.PartitionKeyPath <- sprintf "/%s" partitionKey
    
    // unique keys
    let uk = UniqueKey ()
    uk.Paths.Add "/streamId"
    uk.Paths.Add "/version"

    let ukPolicy = UniqueKeyPolicy()
    ukPolicy.UniqueKeys.Add(uk)
    props.UniqueKeyPolicy <- ukPolicy

    // throughput
    let throughput = throughput |> Throughput.correct |> Nullable<int>

    task {
        let! cont = db.CreateContainerIfNotExistsAsync(props,throughput)
        return cont.Container
    }

let private getStoredProcedureTemplate name =
    task {
        let ass = typeof<CosmoStore.CosmosDb.Configuration>.GetTypeInfo().Assembly
        use stream = ass.GetManifestResourceStream(sprintf "CosmoStore.CosmosDb.StoredProcedures.%s" name)
        use reader = new StreamReader(stream)
        return! reader.ReadToEndAsync()
    }

let private createStoreProcedures (strategy:StorageVersion) (container:Container) =
    task {
        try
            let! _ = container.Scripts.DeleteStoredProcedureAsync(appendEventProcName)
            ()
        with _ -> ()
        let! storedProcTemplate = strategy |> StorageVersion.getStoredProcedureName |> getStoredProcedureTemplate
        let storedProc = storedProcTemplate.Replace("%%CONTAINER_NAME%%", container.Id)
        let! _ = container.Scripts.CreateStoredProcedureAsync(StoredProcedureProperties(Id = appendEventProcName, Body = storedProc))
        return ()
    }

type PositionAndCreated = { Position : int64; Created : DateTime }

module private Version2 =

    let appendEvents (version:StorageVersion) (container:Container) (streamId:StreamId) (expectedVersion:ExpectedVersion<int64>) (events:EventWrite<_> list) = 
        task {
            let jEvents = events |> List.map Serialization.objectToJToken
            let jPosition = expectedVersion |> Serialization.expectedVersionToJObject version
            let pk = PartitionKey(streamId)
            let pars = [|streamId :> obj; jEvents :> obj; jPosition :> obj|]
            let! resp = container.Scripts.ExecuteStoredProcedureAsync<List<PositionAndCreated>> (appendEventProcName, pk, pars)
            return resp.Resource 
                |> List.zip events
                |> List.map (fun (evn,p) -> Conversion.eventWriteToEventRead streamId p.Position p.Created evn)
        }

type VersionAndCreated = { Version : int64; Created : DateTime }
        
module private Version3 =
    

    let appendEvents (version:StorageVersion) (container:Container) (streamId:StreamId) (expectedVersion:ExpectedVersion<int64>) (events:EventWrite<_> list) = 
        task {
            let jEvents = events |> List.map Serialization.objectToJToken
            let jVersion = expectedVersion |> Serialization.expectedVersionToJObject version
            let pk = PartitionKey(streamId)
            let pars = [|streamId :> obj; jEvents :> obj; jVersion :> obj|]
            let! resp = container.Scripts.ExecuteStoredProcedureAsync<List<VersionAndCreated>> (appendEventProcName, pk, pars)
            return resp.Resource 
                |> List.zip events
                |> List.map (fun (evn,p) -> Conversion.eventWriteToEventRead streamId p.Version p.Created evn)
        }
        
let private toEventRead (version:StorageVersion) (doc:JObject) = 
    { 
        Id = doc.Value<string> "id" |> Guid
        CorrelationId = doc.Value<string>("correlationId") |> function | null -> None | x -> Some (Guid x)
        CausationId = doc.Value<string>("causationId") |> function | null -> None | x -> Some (Guid x)
        StreamId = doc.Value<string>("streamId")
        Version = doc.Value<int64>(StorageVersion.getPositionOrVersion version)
        Name = doc.Value<string>("name")
        Data = doc.Value<JToken>("data")
        Metadata = doc.Value<JToken>("metadata") |> function | null -> None | x -> Some x
        CreatedUtc = doc.Value<DateTime>("createdUtc")
    }
    
let private toStream (version:StorageVersion) (x:JObject) = {
    Id = x.Value<string>("streamId")
    LastUpdatedUtc = x.Value<DateTime>("lastUpdatedUtc")
    LastVersion = x.Value<int64>(StorageVersion.getLastPositionOrLastVersion version)
}

let private streamsReadToQuery = function
    | AllStreams -> "", ("@_", null)
    | StartsWith w -> "AND STARTSWITH(e.streamId, @streamId)", ("@streamId", w :> obj)
    | EndsWith w -> "AND ENDSWITH(e.streamId, @streamId)", ("@streamId", w :> obj)
    | Contains t -> "AND CONTAINS(e.streamId, @streamId)", ("@streamId", t :> obj)

let private createQuery q (pars:(string * obj) list) =
    let foldFn (acc:QueryDefinition) (item:string * obj) = acc.WithParameter item
    pars |> List.fold foldFn (QueryDefinition q)

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

let private getStreams (version:StorageVersion) (container:Container) containerName (streamsRead:StreamsReadFilter) =
    task {
        let queryAdd,param = streamsRead |> streamsReadToQuery
        let! res = 
            createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.type = 'Stream' %s" containerName queryAdd) [param]
            |> runQuery<CosmoStore.Stream<_>> (toStream version) container
        return res |> List.sortBy (fun x -> x.Id)
    }

let private streamEventsReadToQuery (version:StorageVersion) (range:EventsReadRange<_>) =
    let posOrver = (StorageVersion.getPositionOrVersion version)
    match range with
    | AllEvents -> ""
    | FromVersion pos -> sprintf "AND e.%s >= %i" posOrver pos 
    | ToVersion pos -> sprintf "AND e.%s <= %i" posOrver pos
    | VersionRange(st,en) -> sprintf "AND e.%s >= %i AND e.%s <= %i" posOrver st posOrver en

let private getEvents (version:StorageVersion) (container:Container) containerName streamId (eventsRead:EventsReadRange<_>) =
    task {
        return! 
            createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.streamId = @streamId AND e.type = 'Event' %s ORDER BY e.%s ASC" containerName (streamEventsReadToQuery version eventsRead) (StorageVersion.getPositionOrVersion version))
                ["@streamId", streamId :> obj]
            |> runQuery<EventRead<_,_>> (toEventRead version) container
    }

let private getEvent storageVersion (container:Container) containerName streamId version =
    task {
        let filter = EventsReadRange.VersionRange(version, version)
        let! events = getEvents storageVersion container containerName streamId filter
        return events.Head
    }

let private getEventsByCorrelationId version (container:Container) containerName (corrId:Guid) =
    task {
        return! createQuery 
            (sprintf "SELECT * FROM %s e WHERE e.correlationId = @corrId AND e.type = 'Event' ORDER BY e.createdUtc ASC" containerName)
            ["@corrId", corrId :> obj]
        |> runQuery<EventRead<_,_>> (toEventRead version) container
    }

let private getStream version (container:Container) containerName streamId =
    task {
        let! streams = 
            createQuery 
                (sprintf "SELECT * FROM %s e WHERE e.type = 'Stream' AND e.streamId = @streamId" containerName) [("@streamId", streamId :> obj)]
            |> runQuery<CosmoStore.Stream<_>> (toStream version) container
        return streams |> List.head
    }

let private getStorageVersion (cont:Container) =
    task {
        let! props = cont.ReadContainerAsync()
        let keys = props.Resource.UniqueKeyPolicy.UniqueKeys
        let key = keys.[0]
        match key.Paths.[1] with
        | "/position" -> return Version2
        | "/version" -> return Version3
        | v -> return failwithf "Invalid uniqueKey '%s'" v
    }

let getEventStore (configuration:Configuration) = 
    let client = new CosmosClient(configuration.ConnectionString)
    let eventAppended = Event<EventRead<JToken,int64>>()

    if configuration.InitializeContainer then
        task {
            let! db = createDatabaseIfNotExists configuration.DatabaseName client
            let! cont = createContainerIfNotExists db configuration.ContainerName configuration.Throughput
            let! version = cont |> getStorageVersion
            let! _ = createStoreProcedures version cont
            return ()
        } |> Async.AwaitTask |> Async.RunSynchronously
    
    let container = client.GetContainer(configuration.DatabaseName, configuration.ContainerName)
    let version =
            container
            |> getStorageVersion
            |> Async.AwaitTask
            |> Async.RunSynchronously
    
    let appendEvents =
        match version with
        | Version2 -> Version2.appendEvents
        | Version3 -> Version3.appendEvents
    
    {
        AppendEvent = fun stream pos event -> task {
            let! events = appendEvents version container stream pos [event]
            events |> List.iter eventAppended.Trigger
            return events |> List.head
        }
                        
        AppendEvents = fun stream pos events -> task {
            if events |> List.isEmpty then return []
            else 
                let! events = appendEvents version container stream pos events
                events |> List.iter eventAppended.Trigger
                return events
        } 

        GetEvent = getEvent version container configuration.ContainerName
        GetEvents = getEvents version container configuration.ContainerName
        GetEventsByCorrelationId = getEventsByCorrelationId version container configuration.ContainerName
        GetStreams = getStreams version container configuration.ContainerName
        GetStream = getStream version container configuration.ContainerName
        EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
    }