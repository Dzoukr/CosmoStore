module CosmoStore.TableStorage.Tests.V2.Program

open System
open CosmoStore
open CosmoStore.TableStorage
open Microsoft.WindowsAzure.Storage

let private tableName = "CosmoStoreTestsV2"

let private conf = Configuration.CreateDefaultForLocalEmulator() |> fun cfg -> { cfg with TableName = tableName }

let getEventStore() =
    let account = CloudStorageAccount.DevelopmentStorageAccount
    let client = account.CreateCloudTableClient()
    let table = client.GetTableReference(tableName)
    try
        table.DeleteIfExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    with _ -> ()
    conf |> EventStore.getEventStore

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
    
    