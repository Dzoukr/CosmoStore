open System.IO
open Fake.Core
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.Core.TargetOperators

open BuildHelpers
open BuildTools

initializeContext()

let clean proj = [ proj </> "bin"; proj </> "obj" ] |> Shell.cleanDirs

let createNuget proj =
    clean proj
    Tools.dotnet "restore --no-cache" proj
    Tools.dotnet "pack -c Release" proj

let publishNuget proj =
    createNuget proj
    let nugetKey =
        match Environment.environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "The Nuget API key must be set in a NUGET_KEY environmental variable"
    let nupkg =
        Directory.GetFiles(proj </> "bin" </> "Release")
        |> Seq.head
        |> Path.GetFullPath
    Tools.dotnet (sprintf "nuget push %s -s nuget.org -k %s" nupkg nugetKey) proj

Target.create "PackCosmoStore" (fun _ -> "src" </> "CosmoStore" |> createNuget)
Target.create "PublishCosmoStore" (fun _ -> "src" </> "CosmoStore" |> publishNuget)
Target.create "TestCosmoStore" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.Tests"))

Target.create "PackTableStorage" (fun _ -> "src" </> "CosmoStore.TableStorage" |> createNuget)
Target.create "PublishTableStorage" (fun _ -> "src" </> "CosmoStore.TableStorage" |> publishNuget)
Target.create "TestTableStorage" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.TableStorage.Tests"))


Target.create "PackCosmosDb" (fun _ -> "src" </> "CosmoStore.CosmosDb" |> createNuget)
Target.create "PublishCosmosDb" (fun _ -> "src" </> "CosmoStore.CosmosDb" |> publishNuget)
Target.create "TestCosmosDb" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.CosmosDb.Tests"))


Target.create "PackMarten" (fun _ -> "src" </> "CosmoStore.Marten" |> createNuget)
Target.create "PublishMarten" (fun _ -> "src" </> "CosmoStore.Marten" |> publishNuget)
Target.create "TestMarten" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.Marten.Tests"))


Target.create "PackInMemory" (fun _ -> "src" </> "CosmoStore.InMemory" |> createNuget)
Target.create "PublishInMemory" (fun _ -> "src" </> "CosmoStore.InMemory" |> publishNuget)
Target.create "TestInMemory" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.InMemory.Tests"))


Target.create "PackLiteDb" (fun _ -> "src" </> "CosmoStore.LiteDb" |> createNuget)
Target.create "PublishLiteDb" (fun _ -> "src" </> "CosmoStore.LiteDb" |> publishNuget)
Target.create "TestLiteDb" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.LiteDb.Tests"))


Target.create "PackServiceStack" (fun _ -> "src" </> "CosmoStore.ServiceStack" |> createNuget)
Target.create "PublishServiceStack" (fun _ -> "src" </> "CosmoStore.ServiceStack" |> publishNuget)
Target.create "TestServiceStack" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.ServiceStack.Tests"))


let dependencies = [
    "TestTableStorage" ==> "PackTableStorage"
    "TestTableStorage" ==> "PublishTableStorage"
    "TestCosmosDb" ==> "PackCosmosDb"
    "TestCosmosDb" ==> "PublishCosmosDb"
    "TestMarten" ==> "PackMarten"
    "TestMarten" ==> "PublishMarten"
    "TestInMemory" ==> "PackInMemory"
    "TestInMemory" ==> "PublishInMemory"
    "TestLiteDb" ==> "PackLiteDb"
    "TestLiteDb" ==> "PublishLiteDb"
    "TestServiceStack" ==> "PackServiceStack"
    "TestServiceStack" ==> "PublishServiceStack"
]

[<EntryPoint>]
let main args = runOrDefault "TestCosmoStore" args