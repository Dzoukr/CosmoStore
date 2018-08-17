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

let getEvent i =
    {
        Id = Guid.NewGuid()
        CorrelationId = Guid.NewGuid()
        Name = sprintf "Created_%i" i
        Data = JValue("TEST STRING")
        Metadata = JValue("TEST STRING META") :> JToken |> Some
    }

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
let ``Appends event`` () =
    let store = conf |> EventStore.getEventStore
    
    getEvent 1
    |> store.AppendEvent "TestSingleStream" ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        Assert.AreEqual(1, er.Position)
    )

[<Test>]
let ``Get streams (all)`` () =
    let store = conf |> EventStore.getEventStore
    let addEventToStream i =
        getEvent 1
        |> store.AppendEvent (sprintf "TestStream%i" i) ExpectedPosition.Any
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    [1..3] |> List.iter addEventToStream
    let streams = store.GetStreams StreamsReadFilter.AllStreams |> Async.AwaitTask |> Async.RunSynchronously
    Assert.AreEqual("TestStream1", streams.Head.Id)
    Assert.AreEqual("TestStream2", streams.[1].Id)
    Assert.AreEqual("TestStream3", streams.[2].Id)
    
[<Test>]
let ``Get streams (startswith)`` () =
    let store = conf |> EventStore.getEventStore
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
let ``Fails to append to existing position`` () =
    let store = conf |> EventStore.getEventStore
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
let ``Fails to append to existing stream if is not expected to exist`` () =
    let store = conf |> EventStore.getEventStore
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
let ``Appends events`` () =
    let store = conf |> EventStore.getEventStore
    let checkCreation acc item =
        Assert.IsTrue(item.CreatedUtc >= acc)
        item.CreatedUtc
    let checkPosition acc (item:EventRead) =
        Assert.IsTrue(item.Position > acc)
        item.Position

    [1..1000]
    |> List.map getEvent
    |> store.AppendEvents "TestMultipleStreamBig" ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> (fun er -> 
        er |> List.fold checkCreation DateTime.MinValue |> ignore
        er |> List.fold checkPosition 0L |> ignore
    )