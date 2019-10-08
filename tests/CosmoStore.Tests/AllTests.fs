module CosmoStore.Tests.AllTests

open CosmoStore
open Expecto

let getTests (name:string) (generator:Domain.TestDataGenerator<_>) (store:EventStore<_,_>) =
    testList name [
       BasicTests.allTests generator store |> testList "Basic Tests"
       Issues.allTests generator store |> testList "Issues"
       Observable.allTests generator store |> testList "Observable" |> testSequenced
    ]