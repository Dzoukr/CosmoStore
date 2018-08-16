module CosmoStore.Tests.CosmosDb

open System
open NUnit.Framework
open CosmoStore.CosmosDb
open CosmoStore
open Newtonsoft.Json.Linq

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

[<Test>]
let ``Creates EventStore`` () =
    conf |> EventStore.getEventStore |> ignore
    Assert.IsTrue(true)

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
let ``Appends events`` () =
    let store = conf |> EventStore.getEventStore
    let checkCreation acc item =
        Assert.IsTrue(item.CreatedUtc >= acc)
        item.CreatedUtc
    let checkPosition acc item =
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