module CosmoStore.Tests.BasicTests

open System
open NUnit.Framework
open CosmoStore
open Newtonsoft.Json.Linq
open Microsoft.Azure.Documents.Client
open CosmoStore.TableStorage

module CosmosDb =
    open CosmoStore.CosmosDb

    let private config = 
        CosmoStore.CosmosDb.Configuration.CreateDefault 
            (Uri "https://localhost:8081") 
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
            |> fun cfg -> { cfg with DatabaseName = "CosmosStoreTests" }

    let getCleanEventStore() =
        let client = new DocumentClient(config.ServiceEndpoint, config.AuthKey)
        try
            do client.DeleteDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(config.DatabaseName, "Events")) 
            |> Async.AwaitTask 
            |> Async.RunSynchronously 
            |> ignore
        with ex -> ()
        config |> EventStore.getEventStore

    let eventStore = getCleanEventStore()

module TableStorage =
    open Microsoft.WindowsAzure.Storage
    let private tableName = "CosmosStoreTests"
    let private conf = Configuration.CreateDefaultForLocalEmulator() |> fun cfg -> { cfg with TableName = tableName }

    let getCleanEventStore() =
        let account = CloudStorageAccount.DevelopmentStorageAccount
        let client = account.CreateCloudTableClient()
        let table = client.GetTableReference(tableName)
        try
            table.DeleteIfExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
        with _ -> ()
        conf |> EventStore.getEventStore
    
    let eventStore = getCleanEventStore()

let getStreamId () = sprintf "TestStream_%A" (Guid.NewGuid())

let getEvent i =
    {
        Id = Guid.NewGuid()
        CorrelationId = Guid.NewGuid()
        Name = sprintf "Created_%i" i
        Data = JValue("TEST STRING")
        Metadata = JValue("TEST STRING META") :> JToken |> Some
    }

let appendEvents store streamId =
    List.map getEvent
    >> store.AppendEvents streamId ExpectedPosition.Any
    >> Async.AwaitTask
    >> Async.RunSynchronously

let checkPosition acc (item:EventRead) =
        Assert.IsTrue(item.Position > acc)
        item.Position

type StoreType =
    | CosmosDB = 0   
    | TableStorage = 1

let getEventStore = function
    | StoreType.CosmosDB -> CosmosDb.eventStore
    | StoreType.TableStorage -> TableStorage.eventStore   

let getCleanEventStore = function
    | StoreType.CosmosDB -> CosmosDb.getCleanEventStore()
    | StoreType.TableStorage -> TableStorage.getCleanEventStore()   

[<Test>]
let ``Appends event`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    getEvent 1
    |> store.AppendEvent streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        Assert.AreEqual(1, er.Position)
    )

[<Test>]
let ``Get event`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let event =
        store.GetEvent streamId 3L
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(3L, event.Position)
    Assert.AreEqual("Created_3", event.Name)

[<Test>]
let ``Get events (all)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId EventsReadRange.AllEvents
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(10, events.Length)
    events |> List.fold checkPosition 0L |> ignore

[<Test>]
let ``Get events (from position)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.FromPosition(6L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(5, events.Length)
    events |> List.fold checkPosition 5L |> ignore
    
[<Test>]
let ``Get events (to position)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.ToPosition(5L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(5, events.Length)
    events |> List.fold checkPosition 0L |> ignore

[<Test>]
let ``Get events (position range)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    [1..10] |> appendEvents store streamId |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.PositionRange(5L,7L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(3, events.Length)
    events |> List.fold checkPosition 4L |> ignore

[<Test>]
let ``Get streams (all)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let addEventToStream i =
        [1..99]
        |> List.map getEvent
        |> store.AppendEvents (sprintf "A_%i" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams StreamsReadFilter.AllStreams |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("A_1", streams.Head.Id)
    Assert.IsTrue(streams.Head.LastUpdatedUtc > DateTime.MinValue)
    Assert.AreEqual(99, streams.Head.LastPosition)
    Assert.AreEqual("A_2", streams.[1].Id)
    Assert.AreEqual("A_3", streams.[2].Id)
    
[<Test>]
let ``Get streams (startswith)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let startsWith = Guid.NewGuid().ToString("N")
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "X%i_%s" i startsWith) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.StarsWith("X2_"+startsWith)) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(sprintf "X2_%s" startsWith, streams.Head.Id)

[<Test>]
let ``Get streams (endswith)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let endsWith = Guid.NewGuid().ToString("N")
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "X%i_%s" i endsWith) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.EndsWith(endsWith)) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(3, streams.Length)
    Assert.AreEqual(sprintf "X1_%s" endsWith, streams.Head.Id)

[<Test>]
let ``Get streams (contains)`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let contains = Guid.NewGuid().ToString("N")
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "C_%s_%i" contains i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.Contains(contains)) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(3, streams.Length)
    Assert.AreEqual(sprintf "C_%s_1" contains, streams.Head.Id)

[<Test>]
let ``Get stream`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = (sprintf "OS_%s" (Guid.NewGuid().ToString("N")))
    [1..10]
    |> List.map getEvent
    |> store.AppendEvents streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore

    let stream = store.GetStream streamId |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(10, stream.LastPosition)
    Assert.AreEqual(streamId, stream.Id)

[<Test>]
let ``Fails to append to existing position`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    Assert.Throws<AggregateException>(fun _ -> 
        getEvent 1
        |> store.AppendEvent streamId ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        getEvent 1
        |> store.AppendEvent streamId (ExpectedPosition.Exact(1L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    ) 
    |> (fun x -> 
        Assert.IsTrue(x.Message.Contains("ESERROR_POSITION_POSITIONNOTMATCH"))
    )

[<Test>]
let ``Fails to append to existing stream if is not expected to exist`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()
    Assert.Throws<AggregateException>(fun _ -> 
        getEvent 1
        |> store.AppendEvent streamId ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        getEvent 1
        |> store.AppendEvent streamId ExpectedPosition.NoStream
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    ) 
    |> (fun x -> 
        Assert.IsTrue(x.Message.Contains("ESERROR_POSITION_STREAMEXISTS"))
    )

[<Test>]
let ``Appends events`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()

    let checkCreation acc item =
        Assert.IsTrue(item.CreatedUtc >= acc)
        item.CreatedUtc

    [1..99]
    |> List.map getEvent
    |> store.AppendEvents streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        er |> List.fold checkCreation DateTime.MinValue |> ignore
        er |> List.fold checkPosition 0L |> ignore
    )

[<Test>]
let ``Appending no events does not affect stream metadata`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getEventStore
    let streamId = getStreamId()
    
    // append single event
    0 |> getEvent |> store.AppendEvent streamId (ExpectedPosition.Exact(1L)) |> Async.AwaitTask |> Async.RunSynchronously |> ignore

    let stream = store.GetStream streamId |> Async.AwaitTask |> Async.RunSynchronously

    // append empty events
    List.empty
    |> List.map getEvent
    |> store.AppendEvents streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore
    
    let streamAfterAppend = store.GetStream streamId |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual(stream, streamAfterAppend)

[<Test>]
    let ``Appending thousand of events can be read back`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
        let store = typ |> getEventStore
        let streamId = getStreamId()
        
        [0..999]
        |> List.map getEvent
        |> List.chunkBySize 99
        |> List.iter (fun evns -> 
            evns |> store.AppendEvents streamId ExpectedPosition.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
        )
        let stream = store.GetStream streamId |> Async.AwaitTask |> Async.RunSynchronously
        Assert.AreEqual(1000, stream.LastPosition)
        
        let evntsBack = store.GetEvents streamId EventsReadRange.AllEvents |> Async.AwaitTask |> Async.RunSynchronously
        Assert.AreEqual(1000, evntsBack.Length)
        