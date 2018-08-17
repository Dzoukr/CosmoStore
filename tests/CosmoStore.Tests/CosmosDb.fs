module CosmoStore.Tests.CosmosDb

open System
open NUnit.Framework
open CosmoStore.CosmosDb
open CosmoStore
open Newtonsoft.Json.Linq
open Microsoft.Azure.Documents.Client

let conf = CosmoStore.CosmosDb.Configuration.CreateDefault 
            (Uri "https://localhost:8081") 
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

let getStore throughput =
    let c = if throughput > 10000 then { conf with Capacity = Unlimited; Throughput = throughput } else { conf with Throughput = throughput }
    c |> EventStore.getEventStore

let getEvent i =
    {
        Id = Guid.NewGuid()
        CorrelationId = Guid.NewGuid()
        Name = sprintf "Created_%i" i
        Data = JValue("TEST STRING")
        Metadata = JValue("TEST STRING META") :> JToken |> Some
    }

let streamId = "TestStream"

let appendEvents store =
    List.map getEvent
    >> store.AppendEvents streamId ExpectedPosition.Any
    >> Async.AwaitTask
    >> Async.RunSynchronously

let checkPosition acc (item:EventRead) =
        Assert.IsTrue(item.Position > acc)
        item.Position

[<SetUp>]
let ``Setup CosmoStore``() =
    let client = new DocumentClient(conf.ServiceEndpoint, conf.AuthKey)
    try
        client.DeleteDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(conf.DatabaseName, "Events")) 
        |> Async.AwaitTask 
        |> Async.RunSynchronously 
        |> ignore
    with _ -> ()

[<Test>]
let ``Appends event`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    
    getEvent 1
    |> store.AppendEvent streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        Assert.AreEqual(1, er.Position)
    )

[<Test>]
let ``Get event`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore

    [1..10] |> appendEvents store |> ignore

    let event =
        store.GetEvent streamId 3L
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(3L, event.Position)
    Assert.AreEqual("Created_3", event.Name)

[<Test>]
let ``Get events (all)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    
    [1..10] |> appendEvents store |> ignore

    let events =
        store.GetEvents streamId EventsReadRange.AllEvents
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(10, events.Length)
    events |> List.fold checkPosition 0L |> ignore

[<Test>]
let ``Get events (from position)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    
    [1..10] |> appendEvents store |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.FromPosition(6L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(5, events.Length)
    events |> List.fold checkPosition 5L |> ignore
    
[<Test>]
let ``Get events (to position)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore

    [1..10] |> appendEvents store |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.ToPosition(5L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(5, events.Length)
    events |> List.fold checkPosition 0L |> ignore

[<Test>]
let ``Get events (position range)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore

    [1..10] |> appendEvents store |> ignore

    let events =
        store.GetEvents streamId (EventsReadRange.PositionRange(5L,7L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    Assert.AreEqual(3, events.Length)
    events |> List.fold checkPosition 4L |> ignore

[<Test>]
let ``Get streams (all)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    let addEventToStream i =
        [1..1000]
        |> List.map getEvent
        |> store.AppendEvents (sprintf "TestStream%i" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams StreamsReadFilter.AllStreams |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("TestStream1", streams.Head.Id)
    Assert.IsTrue(streams.Head.LastUpdatedUtc > DateTime.MinValue)
    Assert.AreEqual(1000, streams.Head.LastPosition)
    Assert.AreEqual("TestStream2", streams.[1].Id)
    Assert.AreEqual("TestStream3", streams.[2].Id)
    
[<Test>]
let ``Get streams (startswith)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "%iTestStream" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.StarsWith("2")) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("2TestStream", streams.Head.Id)

[<Test>]
let ``Get streams (endswith)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "TestStream%i" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.EndsWith("2")) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("TestStream2", streams.Head.Id)

[<Test>]
let ``Get streams (contains)`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "Test%iStream" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams (StreamsReadFilter.Contains("2")) |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("Test2Stream", streams.Head.Id)

[<Test>]
let ``Fails to append to existing position`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    Assert.Throws<AggregateException>(fun _ -> 
        getEvent 1
        |> store.AppendEvent "TestSingleStream" ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        getEvent 1
        |> store.AppendEvent "TestSingleStream" (ExpectedPosition.Exact(1L))
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    ) 
    |> (fun x -> 
        Assert.IsTrue(x.Message.Contains("ESERROR_POSITION_POSITIONNOTMATCH"))
    )

[<Test>]
let ``Fails to append to existing stream if is not expected to exist`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    Assert.Throws<AggregateException>(fun _ -> 
        getEvent 1
        |> store.AppendEvent "TestSingleStream" ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        getEvent 1
        |> store.AppendEvent "TestSingleStream" ExpectedPosition.NoStream
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    ) 
    |> (fun x -> 
        Assert.IsTrue(x.Message.Contains("ESERROR_POSITION_STREAMEXISTS"))
    )

[<Test>]
let ``Appends events`` ([<Values(1000, 100000)>] (tp:int)) =
    let store = tp |> getStore
    let checkCreation acc item =
        Assert.IsTrue(item.CreatedUtc >= acc)
        item.CreatedUtc

    [1..1000]
    |> List.map getEvent
    |> store.AppendEvents "TestMultipleStreamBig" ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        er |> List.fold checkCreation DateTime.MinValue |> ignore
        er |> List.fold checkPosition 0L |> ignore
    )