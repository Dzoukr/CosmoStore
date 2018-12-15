module internal CosmoStore.Observable

open System
open FSharp.Control.Tasks.V2

let hookEvents (e:Event<EventRead>) (events:Threading.Tasks.Task<EventRead list>) =
    task {
        let! events = events
        events |> List.iter e.Trigger
        return events
    }

let hookEvent (e:Event<EventRead>) (events:Threading.Tasks.Task<EventRead list>) =
    task {
        let! events = events
        let single = events |> List.head 
        single |> e.Trigger
        return single
    }