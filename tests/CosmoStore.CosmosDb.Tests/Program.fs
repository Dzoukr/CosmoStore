module CosmoStore.CosmosDb.Tests.Program

open System
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.CosmosDb
open Microsoft.Azure.Cosmos

let private config = 
    CosmoStore.CosmosDb.Configuration.CreateDefault 
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
    |> fun cfg -> { cfg with DatabaseName = "CosmoStoreTests"; Throughput = 10000; ContainerName = "MyEvents" }

let private getCleanEventStore() =
    let client = new CosmosClient(config.ConnectionString)
    try
        client.GetContainer(config.DatabaseName, config.ContainerName)
        |> (fun x -> x.DeleteContainerAsync())
        |> Async.AwaitTask 
        |> Async.RunSynchronously 
        |> ignore
    with ex -> ()
    config |> EventStore.getEventStore
    
let testConfig = 
    { Expecto.Tests.defaultConfig with 
        parallelWorkers = 4
        verbosity = LogLevel.Debug }

let cfg = Domain.defaultTestConfiguration getCleanEventStore

[<EntryPoint>]
let main _ = 
    (cfg, "Cosmos DB") 
    |> AllTests.getTests 
    |> runTests testConfig