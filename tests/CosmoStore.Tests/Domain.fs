module CosmoStore.Tests.Domain

open System
open CosmoStore
open Newtonsoft.Json.Linq

type TestConfiguration = {
    GetStreamId : unit -> string
    GetEvent : int -> EventWrite
    Store : EventStore
    GetEmptyStore : unit -> EventStore
}

let private getEvent i =
    let corr,caus =
        match i%2, i%3 with
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

let defaultTestConfiguration getEmptyStoreFn = {
    GetStreamId = fun _ -> sprintf "TestStream_%A" (Guid.NewGuid())
    GetEvent = getEvent
    Store = getEmptyStoreFn()
    GetEmptyStore = getEmptyStoreFn
}

module ExpectoHelpers =
    open Expecto

    let equal x y = Expect.equal x y (sprintf "%A = %A" x y)
    let notEqual x y = Expect.notEqual x y (sprintf "%A != %A" x y)
    let isTrue x = Expect.isTrue x (sprintf "%A = true" x)
    let private checkPosition acc (item:EventRead) =
        isTrue(item.Position > acc)
        item.Position
    let private checkCreation acc item =
        isTrue (item.CreatedUtc >= acc)
        item.CreatedUtc
    let areAscending = List.fold checkPosition 0L >> ignore 
    let areNewer = List.fold checkCreation DateTime.MinValue >> ignore 