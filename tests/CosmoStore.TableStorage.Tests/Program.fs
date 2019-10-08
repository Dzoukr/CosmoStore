module CosmoStore.TableStorage.Tests.Program

open System
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.TableStorage
open Microsoft.WindowsAzure.Storage

let private tableName = "CosmoStoreTests"

let private conf = Configuration.CreateDefaultForLocalEmulator() |> fun cfg -> { cfg with TableName = tableName }

let getCleanEventStore() =
    let account = CloudStorageAccount.DevelopmentStorageAccount
    let client = account.CreateCloudTableClient()
    let table = client.GetTableReference(tableName)
    try
        table.DeleteIfExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    with _ -> ()
    conf |> EventStore.getEventStore
 
let testConfig = 
    { Expecto.Tests.defaultConfig with 
        parallelWorkers = 2
        verbosity = LogLevel.Debug }

[<EntryPoint>]
let main _ = 
    AllTests.getTests "Table Storage" Generator.defaultGenerator (getCleanEventStore())
    |> runTests testConfig