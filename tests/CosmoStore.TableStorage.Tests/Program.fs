module CosmoStore.TableStorage.Tests.Program

open System
open System.Diagnostics
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.TableStorage
open Microsoft.WindowsAzure.Storage

let private v3config = Configuration.CreateDefaultForLocalEmulator() |> fun cfg -> { cfg with TableName = "CosmoStoreTests" }
let private v2config = { v3config with TableName = "CosmoStoreTestsV2" }

let getEventStore cleanup conf =
    let account = CloudStorageAccount.DevelopmentStorageAccount
    let client = account.CreateCloudTableClient()
    let table = client.GetTableReference(conf.TableName)
    if cleanup then
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
    // run v3 tests
    AllTests.getTests "Table Storage - V3" Generator.defaultGenerator (getEventStore true v3config) |> runTests testConfig |> ignore
    // write "old" events
    Process.Start("dotnet", "run -p tests/CosmoStore.TableStorage.Tests.V2").WaitForExit()
    // run v2 tests
    AllTests.getTests "Table Storage - V2" Generator.defaultGenerator (getEventStore false v2config) |> runTests testConfig |> ignore
    0