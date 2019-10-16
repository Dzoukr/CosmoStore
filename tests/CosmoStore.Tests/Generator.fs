module CosmoStore.Tests.Generator

open System
open Domain
open CosmoStore
open Newtonsoft.Json.Linq

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
        Data = CosmoStore.Tests.Data.json
        Metadata = JValue("TEST STRING META") :> JToken |> Some
    }

let defaultGenerator =
    {
        GetStreamId = fun _ -> sprintf "TestStream_%A" (Guid.NewGuid())
        GetEvent = getEvent
    }