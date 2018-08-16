namespace CosmoStore.CosmosDb

open System

type Capacity =
    | Fixed
    | Unlimited
    with
        member x.ThroughputLimits =
            match x with
            | Fixed -> 400, 10000
            | Unlimited -> 1000, 100000
        
        member x.CorrectThroughput (i:int) =
            let throughput = Math.Round((decimal i) / 100m, 0) * 100m |> int
            let min,max = x.ThroughputLimits
            if throughput > max then max
            else if throughput < min then min
            else throughput
        
        member x.UsePartitionKey =
            match x with
            | Fixed -> false
            | Unlimited -> true

type Configuration = {
    DatabaseName : string
    ServiceEndpoint : Uri
    AuthKey : string
    Capacity: Capacity
    Throughput: int
}
with
    static member CreateDefault serviceEndpoint authKey = {
        DatabaseName = "EventStore"
        ServiceEndpoint = serviceEndpoint
        AuthKey = authKey
        Capacity = Fixed
        Throughput = Fixed.ThroughputLimits |> fst
    }
