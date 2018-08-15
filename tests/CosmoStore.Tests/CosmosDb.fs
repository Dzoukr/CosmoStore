module CosmoStore.Tests.CosmosDb

open System
open NUnit.Framework
open CosmoStore.CosmosDb.EventStore

let conf = CosmoStore.CosmosDb.EventStore.Configuration.CreateDefault (Uri "https://localhost:8081") "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

[<Test>]
let ``Creates EventStore`` () =
    conf |> getEventStore |> ignore
    Assert.IsTrue(true)