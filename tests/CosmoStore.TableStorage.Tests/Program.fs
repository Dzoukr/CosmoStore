module CosmoStore.TableStorage.Tests.Program

open System
open System.Diagnostics
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.TableStorage
open Azure.Data.Tables

let private v3config = Configuration.CreateDefaultForLocalEmulator() |> fun cfg -> { cfg with TableName = "CosmoStoreTests" }
let private v2config = { v3config with TableName = "CosmoStoreTestsV2" }

let getEventStore cleanup conf =
    if cleanup then
        try
            let tableServiceClient =
                TableServiceClient ("DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1")
            tableServiceClient.DeleteTable(conf.TableName) |> ignore
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