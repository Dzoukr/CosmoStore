open CosmoStore
open CosmoStore.InMemory.EventStore
open CosmoStore.Tests
open Expecto
open Expecto.Logging
open Newtonsoft.Json.Linq
open System
open System.Collections.Concurrent

let private getCleanEventStore() =
    getEventStore
        { InMemoryStreams = ConcurrentDictionary<string, Stream<_>>()
          InMemoryEvents = ConcurrentDictionary<Guid, EventRead<JToken, int64>>() }

let testConfig =
    { Expecto.Tests.defaultConfig with
          parallelWorkers = 2
          verbosity = LogLevel.Debug }

[<EntryPoint>]
let main _ =
    AllTests.getTests "InMemory" Generator.defaultGenerator (getCleanEventStore()) |> runTests testConfig
