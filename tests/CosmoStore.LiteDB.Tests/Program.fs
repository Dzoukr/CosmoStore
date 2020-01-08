module CosmoStore.LiteDb.Tests.Program

open System
open System.Collections.Generic
open Expecto
open Expecto.Logging
open CosmoStore
open CosmoStore.Tests
open CosmoStore.LiteDb.EventStore
open CosmoStore.LiteDb
open CosmoStore.Tests.Domain
open LiteDB
open System.Linq
open Newtonsoft.Json.Linq

let testConfig =
    { Expecto.Tests.defaultConfig with
          parallelWorkers = 2
          verbosity = LogLevel.Debug }

let private getCleanEventStore() =
    getEventStore Configuration.Empty

let rec private toDictionary (token:JToken) : Dictionary<string, BsonValue> =
    let dict = token.ToObject<Dictionary<string, BsonValue>>()
    for item in dict.Where(fun x -> x.Value.IsNull).ToList() do
       dict.[item.Key] <- BsonValue(toDictionary(token.[item.Key])) 
    dict

let private getEvent i =
    let corr, caus =
        match i % 2, i % 3 with
        | 0, _ -> (Some <| Guid.NewGuid()), None
        | _, 0 -> None, (Some <| Guid.NewGuid())
        | _ -> None, None

    {
        Id = Guid.NewGuid()
        CorrelationId = corr
        CausationId = caus
        Name = sprintf "Created_%i" i
        Data = BsonValue("Some cool data, right?")
        Metadata = BsonValue("TEST STRING META") |> Some
    }

let liteDbGenerator =
    {
        GetStreamId = fun _ -> sprintf "TestStream_%A" (Guid.NewGuid())
        GetEvent = getEvent
    } : TestDataGenerator<BsonValue>

[<EntryPoint>]
let main _ =
    AllTests.getTests "LiteDB" liteDbGenerator (getCleanEventStore()) |> runTests testConfig
