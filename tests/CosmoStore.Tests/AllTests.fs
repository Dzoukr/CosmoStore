module CosmoStore.Tests.AllTests

open CosmoStore
open Expecto

let getTests (name:string) (tools:Domain.TestDataGenerator<_>) (store:EventStore<_,_>) =
    testList name [
       BasicTests.allTests |> List.map (fun t -> t cfg) |> testList "Basic Tests"
       Issues.allTests cfg |> testList "Issues"
       Observable.allTests cfg |> testList "Observable" |> testSequenced
    ]