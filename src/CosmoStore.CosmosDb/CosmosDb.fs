namespace CosmoStore.CosmosDb

open System

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
