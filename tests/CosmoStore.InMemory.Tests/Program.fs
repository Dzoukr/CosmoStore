open CosmoStore
open CosmoStore.InMemory.EventStore
open CosmoStore.Tests
open Expecto
open Expecto.Logging
open System
open System.Collections.Concurrent

let private getCleanEventStore() =
    getEventStore {InMemoryStreams = new ConcurrentDictionary<string,Stream>(); InMemoryEvents = new ConcurrentDictionary<Guid,EventRead>()}

let testConfig =
    { Expecto.Tests.defaultConfig with
        parallelWorkers = 2
        verbosity = LogLevel.Debug }

let cfg() =  Domain.defaultTestConfiguration getCleanEventStore

[<EntryPoint>]
let main _ =
    try 
        (cfg(), "Marten")
        |> AllTests.getTests
        |> runTests testConfig
    with
        exn -> printfn "%A" exn |> fun _ -> 0
