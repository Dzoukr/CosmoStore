module CosmoStore.Tests.Issues

open System
open CosmoStore
open Expecto
open Domain
open Domain.ExpectoHelpers

let allTests (gen:TestDataGenerator<_>) eventStore = 
    [
        testTask "Can read back Events stored without metadata" {
            let streamId = gen.GetStreamId()
            let event = 1 |> gen.GetEvent |> (fun e -> { e with Metadata = None })
            let! (e : EventRead<_,_>) = event |> eventStore.AppendEvent streamId Any
            equal None e.Metadata
        }

        testTask "NoStream Version check works for non-existing stream" {
            let streamId = gen.GetStreamId()
            let event = 1 |> gen.GetEvent |> (fun e -> { e with Metadata = None })
            let! (e : EventRead<_,_>) = event |> eventStore.AppendEvent streamId NoStream
            equal None e.Metadata
        }
    ]