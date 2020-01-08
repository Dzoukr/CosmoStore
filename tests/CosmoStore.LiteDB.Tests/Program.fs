module CosmoStore.LiteDB.Tests.Program

open System
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open CosmoStore.LiteDb.EventStore
open CosmoStore.LiteDb

let testConfig =
    { Expecto.Tests.defaultConfig with
          parallelWorkers = 2
          verbosity = LogLevel.Debug }

let private getCleanEventStore() =
    getEventStore Configuration.Empty




[<EntryPoint>]
let main _ =
    AllTests.getTests "LiteDB" Generator.defaultGenerator (getCleanEventStore()) |> runTests testConfig
