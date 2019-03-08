module CosmoStore.LiteDB.Tests.Program
open System
open Expecto
open Expecto.Logging
open CosmoStore.Tests
open Newtonsoft.Json.Linq
open CosmoStore
open FSharp.Control.Tasks.V2
open System.Reactive.Linq
open System.Reactive.Concurrency


let testConfig = 
    { Expecto.Tests.defaultConfig with 
        parallelWorkers = 2
        verbosity = LogLevel.Debug }

let private getCleanEventStore() = 
    {
        AppendEvent = fun stream pos event -> task {
            return {
            Id = Guid.Empty
            CorrelationId = None
            CausationId = None
            StreamId = ""
            Position = 0L
            Name  = ""
            Data = JToken.FromObject(null)
            Metadata = None
            CreatedUtc  = DateTime.Now }
        }
        AppendEvents = fun stream pos events -> task {return []}
        GetEvent = fun stream pos -> task {
            return {
            Id = Guid.Empty
            CorrelationId = None
            CausationId = None
            StreamId = ""
            Position = 0L
            Name  = ""
            Data = JToken.FromObject(null)
            Metadata = None
            CreatedUtc  = DateTime.Now }
        }
        GetEvents = fun stream range -> task {return []} 
        GetEventsByCorrelationId = fun stream -> task {return []}
        GetStreams = fun filter -> task {return []}
        GetStream = fun stream -> task {
             return {
                 Id  = ""
                 LastPosition = 0L
                 LastUpdatedUtc = DateTime.Now
             }
        } 
        EventAppended = Observable.Empty()
    }

let cfg = Domain.defaultTestConfiguration getCleanEventStore

[<EntryPoint>]
let main _ =
    (cfg, "Lite DB") 
    |> AllTests.getTests 
    |> runTests testConfig
