module CosmoStore.Tests.Domain

open System
open CosmoStore

type TestDataGenerator<'payload> = {
    GetStreamId: unit -> string
    GetEvent: int -> EventWrite<'payload>
}

module ExpectoHelpers =
    open Expecto

    let equal x y = Expect.equal x y (sprintf "%A = %A" x y)
    let notEqual x y = Expect.notEqual x y (sprintf "%A != %A" x y)
    let isTrue x = Expect.isTrue x (sprintf "%A = true" x)
    let private checkPosition acc (item: EventRead<_,_>) =
        isTrue (item.Version > acc)
        item.Version
    let private checkCreation acc item =
        isTrue (item.CreatedUtc >= acc)
        item.CreatedUtc
    let areAscending list = list |> List.fold checkPosition 0L |> ignore
    let areNewer list = list |> List.fold checkCreation DateTime.MinValue |> ignore
