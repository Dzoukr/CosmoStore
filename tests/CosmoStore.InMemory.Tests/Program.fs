open CosmoStore
open CosmoStore.InMemory.EventStore
open CosmoStore.Tests
open Expecto
open Expecto.Logging
open System
open System.Collections.Concurrent

let private getCleanEventStore() =
    getEventStore {InMemoryStreams = new ConcurrentDictionary<string,Stream<_>>(); InMemoryEvents = new ConcurrentDictionary<Guid,EventRead<_,_>>()}

let testConfig =
    { Expecto.Tests.defaultConfig with
        parallelWorkers = 2
        verbosity = LogLevel.Debug }

[<EntryPoint>]
let main _ =
    AllTests.getTests "InMemory" Generator.defaultGenerator (getCleanEventStore())
    |> runTests testConfig