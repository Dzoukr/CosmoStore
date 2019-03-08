module CosmoStore.LiteDB.Tests.Program
open System
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.LiteDB.EventStore
open CosmoStore.LiteDB




let testConfig = 
    { Expecto.Tests.defaultConfig with 
        parallelWorkers = 2
        verbosity = LogLevel.Debug }

let private getCleanEventStore() = 
    getEventStore Configuration.Empty

let cfg = Domain.defaultTestConfiguration getCleanEventStore

[<EntryPoint>]
let main _ =
    (cfg, "Lite DB") 
    |> AllTests.getTests 
    |> runTests testConfig
