module CosmoStore.Tests.Issues

open System
open CosmoStore
open Expecto
open Domain
open Domain.ExpectoHelpers

let allTests (cfg:TestDataGenerator<_,_>) = 
    [
        testTask "Can read back Events stored without metadata" {
            let streamId = cfg.GetStreamId()
            let event = 1 |> cfg.GetEvent |> (fun e -> { e with Metadata = None })
            let! (e : EventRead<_,_>) = event |> cfg.Store.AppendEvent streamId Any
            equal None e.Metadata
        }

        testTask "NoStream Position check works for non-existing stream" {
            let streamId = cfg.GetStreamId()
            let event = 1 |> cfg.GetEvent |> (fun e -> { e with Metadata = None })
            let! (e : EventRead<_,_>) = event |> cfg.Store.AppendEvent streamId NoStream
            equal None e.Metadata
        }

    ]