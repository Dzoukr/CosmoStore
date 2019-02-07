module CosmoStore.Tests.AllTests

open Expecto

let getTests (cfg, name) =
    testList name [
       BasicTests.allTests |> List.map (fun t -> t cfg) |> testList "Basic Tests"
       Issues.allTests cfg |> testList "Issues"
    ]