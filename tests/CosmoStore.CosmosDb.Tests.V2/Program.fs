module CosmoStore.CosmosDb.Tests.V2.Program

open System
open CosmoStore
open Microsoft.Azure.Documents.Client

let config =
    CosmoStore.CosmosDb.Configuration.CreateDefault (Uri("https://localhost:8081")) "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
    |> fun cfg -> { cfg with DatabaseName = "CosmoStoreTests"; Throughput = 10000; CollectionName = "MyV2Events" }

let private getEventStore() =
    let client = new DocumentClient(config.ServiceEndpoint, config.AuthKey)
    try
        do client.DeleteDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(config.DatabaseName, config.CollectionName)) 
        |> Async.AwaitTask 
        |> Async.RunSynchronously 
        |> ignore
    with ex -> ()
    config |> CosmoStore.CosmosDb.EventStore.getEventStore

[<EntryPoint>]

let main _ =
    let store = getEventStore()
    let streamId = Guid.NewGuid().ToString("N")
    let getEvent i =
        {
            Id = Guid.NewGuid()
            CorrelationId = None
            CausationId = None
            Name = "V2_Event_" + i.ToString()
            Data = CosmoStore.Tests.Data.json
            Metadata = None
        } : EventWrite
    let events = [1..10] |> List.map getEvent
    Console.WriteLine "Writing 10 events for version 2"
    store.AppendEvents streamId ExpectedPosition.Any events
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore
    Console.WriteLine "10 events for version 2 written"
    0
    
    