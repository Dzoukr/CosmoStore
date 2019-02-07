module CosmoStore.CosmosDb.Tests.Program

open System
open Expecto
open Expecto.Logging

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 4
                        verbosity = LogLevel.Debug }

module CosmosDb =
    open CosmoStore.CosmosDb
    open Microsoft.Azure.Documents.Client

    let private config = 
        CosmoStore.CosmosDb.Configuration.CreateDefault 
            (Uri "https://localhost:8081") 
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
            |> fun cfg -> { cfg with DatabaseName = "CosmosStoreTests" }

    let private getCleanEventStore() =
        let client = new DocumentClient(config.ServiceEndpoint, config.AuthKey)
        try
            do client.DeleteDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(config.DatabaseName, "Events")) 
            |> Async.AwaitTask 
            |> Async.RunSynchronously 
            |> ignore
        with ex -> ()
        config |> EventStore.getEventStore
    
    let cosmosDbCfg = Domain.defaultTestConfiguration getCleanEventStore

[<EntryPoint>]
let main args = 
    let testsForCfg (cfg, name) =
        testList name [
           BasicTests.allTests |> List.map (fun t -> t cfg) |> testList "Basic Tests"
           Issues.allTests cfg |> testList "Issues"
        ] 

    [ CosmosDb.cosmosDbCfg, "Cosmos DB" ]
    |> List.map testsForCfg
    |> testList ""
    |> runTests testConfig        