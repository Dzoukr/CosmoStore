#r "paket: groupref Build //"

#load ".fake/build.fsx/intellisense.fsx"
open Fake
open Fake.Core
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO.FileSystemOperators

module Target =
    let runParallel n t = Target.run n t []   

let assertProcessResult (p : ProcessResult) =
    if not p.OK then
        p.Errors
        |> Seq.iter(Trace.traceError)
        failwithf "Failed with exitcode: %d" p.ExitCode
let run par = par |> DotNet.exec id "run" |> assertProcessResult
let build par = par |> DotNet.build id |> ignore

type Project = {
    Src : string
    Tests : string
    ReleaseNotes : ReleaseNotes.ReleaseNotes
    Tags : string
    Package : string
    Description : string
}

let createProject projectName tags desc = 
    let src = "./src" |> Fake.IO.Path.getFullName
    let tests = "./tests" |> Fake.IO.Path.getFullName
    {
        Src = src </> projectName
        Tests = tests </> projectName
        ReleaseNotes = src </> projectName </> "RELEASE_NOTES.md" |> ReleaseNotes.load
        Tags = "F# FSharp EventStore EventSourcing " + tags
        Package = projectName
        Description = desc
    }

let cosmoStore = createProject "CosmoStore" "" "F# Event Store API definition (for storage provider specific implementations check CosmoStore.* packages)"
let tableStorage = createProject "CosmoStore.TableStorage" "Azure TableStorage" "F# Event Store for Azure Table Storage"
let cosmosDb = createProject "CosmoStore.CosmosDb" "Azure Cosmos DB" "F# Event Store for Azure Cosmos DB"
let martenStore = createProject "CosmoStore.Marten" "Marten Postgresql Store" "F# Event Store for Marten Postgresql DB"
let inMemoryStore = createProject "CosmoStore.InMemory" "In Memory Store" "F# Event Store for In Memory Concurrent Dictionary"
let liteDBStore = createProject "CosmoStore.LiteDb" "LiteDB Store" "F# Event Store for Lite DB"
let serviceStackStore = createProject "CosmoStore.ServiceStack" "ServiceStack Store" "F# Event Store for All DB"

// building projects
Target.create "BuildCosmoStore" (fun _ -> cosmoStore.Src |> build)
Target.create "BuildTableStorage" (fun _ -> tableStorage.Src |> build)
Target.create "BuildCosmosDb" (fun _ -> cosmosDb.Src |> build)
Target.create "BuildMartenStore" (fun _ -> martenStore.Src |> build)
Target.create "BuildInMemoryStore" (fun _ -> inMemoryStore.Src |> build)
Target.create "BuildLiteDBStore" (fun _ -> liteDBStore.Src |> build)
Target.create "BuildServiceStackStore" (fun _ -> serviceStackStore.Src |> build)
Target.create "BuildAll" (fun _ -> 
    [
        "BuildCosmoStore"
        "BuildTableStorage"
        "BuildCosmosDb"
        "BuildMartenStore"
        "BuildInMemoryStore"
        "BuildLiteDBStore"
        "BuildServiceStackStore"
    ] |> List.iter (Target.runParallel 3)
)

// running tests
Target.create "TestTableStorage" (fun _ -> run "-p tests/CosmoStore.TableStorage.Tests")
Target.create "TestCosmosDb" (fun _ -> run "-p tests/CosmoStore.CosmosDb.Tests")
Target.create "TestMartenStore" (fun _ -> run "-p tests/CosmoStore.Marten.Tests")
Target.create "TestInMemoryStore" (fun _ -> run "-p tests/CosmoStore.InMemory.Tests")
Target.create "TestLiteDBStore" (fun _ -> run "-p tests/CosmoStore.LiteDB.Tests")
Target.create "TestServiceStackStore" (fun _ -> run "-p tests/CosmoStore.ServiceStack.Tests")
Target.create "TestAll" (fun _ -> 
    [
        "TestTableStorage"
        "TestCosmosDb"
        "TestMartenStore"
        "TestInMemoryStore"
        "TestLiteDBStore"
        "TestServiceStackStore"
    ] 
    |> List.iter (Target.runParallel 2)
)

// nugets
let createNuget (project:Project) =
    let args = 
        [
            sprintf "Title=\"%s\"" project.Package
            sprintf "Description=\"%s\"" project.Description
            sprintf "Summary=\"%s\"" project.Description
            sprintf "PackageVersion=\"%s\"" project.ReleaseNotes.NugetVersion
            sprintf "PackageReleaseNotes=\"%s\"" (project.ReleaseNotes.Notes |> String.toLines)
            "PackageLicenseUrl=\"http://github.com/dzoukr/CosmoStore/blob/master/LICENSE.md\""
            "PackageProjectUrl=\"http://github.com/dzoukr/CosmoStore\"" 
            "PackageIconUrl=\"\""
            "PackageIconUrl=\"https://raw.githubusercontent.com/Dzoukr/CosmoStore/master/logo.png\""
            sprintf "PackageTags=\"%s\"" project.Tags
            "Copyright=\"Roman Provazník - 2020\""
        ] 
        |> List.map (fun x -> "/p:" + x)
        |> String.concat " "

    project.Src 
    |> DotNet.pack (fun p -> { p with Configuration = DotNet.Custom "Release"; OutputPath = Some "../../nuget"; Common = { p.Common with CustomParams = Some args } })

Target.create "NugetCosmoStore" (fun _ -> cosmoStore |> createNuget)
Target.create "NugetTableStorage" (fun _ -> tableStorage |> createNuget)
Target.create "NugetCosmosDb" (fun _ -> cosmosDb |> createNuget)
Target.create "NugetMartenStore" (fun _ -> martenStore |> createNuget)
Target.create "NugetInMemoryStore" (fun _ -> inMemoryStore |> createNuget)
Target.create "NugetLiteDBStore" (fun _ -> liteDBStore |> createNuget)
Target.create "NugetServiceStackStore" (fun _ -> liteDBStore |> createNuget)
Target.create "NugetAll" (fun _ -> 
    [
        "NugetCosmoStore"
        "NugetTableStorage"
        "NugetCosmosDb"
        "NugetMartenStore"
        "NugetInMemoryStore"
        "NugetLiteDBStore"
        "NugetServiceStackStore"
    ] |> List.iter (Target.runParallel 0)
)    

Fake.Core.Target.create "Clean" (fun _ -> 
    !! "src/*/bin"
    ++ "src/*/obj"
    ++ "tests/*/bin"
    ++ "tests/*/obj"
    |> Shell.deleteDirs
)

"Clean" ==> "TestTableStorage" ==> "NugetTableStorage"
"Clean" ==> "TestCosmosDb" ==> "NugetCosmosDb"
"Clean" ==> "TestMartenStore" ==> "NugetMartenStore"
"Clean" ==> "TestInMemoryStore" ==> "NugetInMemoryStore"
"Clean" ==> "TestLiteDBStore" ==> "NugetLiteDBStore"
"Clean" ==> "TestServiceStackStore" ==> "NugetServiceStackStore"

// start build
Fake.Core.Target.runOrDefaultWithArguments "BuildAll"
