namespace CosmoStore.CosmosDb

open System

type internal StorageVersion =
    | Version2
    | Version3

module internal StorageVersion =
    let getStoredProcedureName (strategy:StorageVersion) =
        match strategy with
        | Version2 -> "AppendEventsV2.js"
        | Version3 -> "AppendEventsV3.js"
    let getPositionOrVersion (strategy:StorageVersion) =
        match strategy with
        | Version2 -> "position"
        | Version3 -> "version"
    let getLastPositionOrLastVersion (strategy:StorageVersion) =
        match strategy with
        | Version2 -> "lastPosition"
        | Version3 -> "lastVersion"
    

module internal Throughput =
    let min = 400
    let max = 1_000_000

    let correct (i:int) =
        let throughput = Math.Round((decimal i) / 100m, 0) * 100m |> int
        if throughput > max then max
        else if throughput < min then min
        else throughput

type Configuration = {
    DatabaseName : string
    ContainerName : string
    ConnectionString : string
    Throughput : int
    InitializeContainer : bool
}
with
    static member CreateDefault connectionString = {
        DatabaseName = "EventStore"
        ContainerName = "Events"
        ConnectionString = connectionString
        Throughput = Throughput.min
        InitializeContainer = true
    }
