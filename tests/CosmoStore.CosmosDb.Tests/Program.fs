module CosmoStore.CosmosDb.Tests.Program

open System
open System.Diagnostics
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.CosmosDb
open Microsoft.Azure.Cosmos

let private v3config = 
    CosmoStore.CosmosDb.Configuration.CreateDefault 
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
    |> fun cfg -> { cfg with DatabaseName = "CosmoStoreTests"; Throughput = 10000; ContainerName = "MyEvents" }

let private v2config = { v3config with ContainerName = "MyV2Events" } 

let private getEventStore cleanup config =
    let client = new CosmosClient(config.ConnectionString)
    if cleanup then
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

[<EntryPoint>]
let main _ = 
    // run v3 tests
    AllTests.getTests "Cosmos DB - V3" Generator.defaultGenerator (getEventStore true v3config) |> runTests testConfig |> ignore
    // write "old" events
    Process.Start("dotnet", "run -p tests/CosmoStore.CosmosDb.Tests.V2").WaitForExit()
    // run v2 tests
    AllTests.getTests "Cosmos DB - V2" Generator.defaultGenerator (getEventStore false v2config) |> runTests testConfig |> ignore
    0
