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

let run par = par |> DotNet.exec id "run" |> ignore
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

// building projects
Target.create "BuildCosmoStore" (fun _ -> cosmoStore.Src |> build)
Target.create "BuildTableStorage" (fun _ -> tableStorage.Src |> build)
Target.create "BuildCosmosDb" (fun _ -> cosmosDb.Src |> build)
Target.create "BuildMartenStore" (fun _ -> martenStore.Src |> build)
Target.create "BuildAll" (fun _ -> 
    [
        "BuildCosmoStore"
        "BuildTableStorage"
        "BuildCosmosDb"
        "BuildMartenStore"
    ] |> List.iter (Target.runParallel 3)
)

// running tests
Target.create "TestTableStorage" (fun _ -> run "-p tests/CosmoStore.TableStorage.Tests")
Target.create "TestCosmosDb" (fun _ -> run "-p tests/CosmoStore.CosmosDb.Tests")
Target.create "TestMartenStore" (fun _ -> run "-p tests/CosmoStore.Marten.Tests")
Target.create "TestAll" (fun _ -> 
    [
        "TestTableStorage"
        "TestCosmosDb"
        "TestMartenStore"
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
            "Copyright=\"Roman Provazník - 2019\""
            "Authors=\"Roman Provazník\""
        ] 
        |> List.map (fun x -> "/p:" + x)
        |> String.concat " "

    project.Src 
    |> DotNet.pack (fun p -> { p with Configuration = DotNet.Custom "Release"; OutputPath = Some "../../nuget"; Common = { p.Common with CustomParams = Some args } })

Target.create "NugetCosmoStore" (fun _ -> cosmoStore |> createNuget)
Target.create "NugetTableStorage" (fun _ -> tableStorage |> createNuget)
Target.create "NugetCosmosDb" (fun _ -> cosmosDb |> createNuget)
Target.create "NugetMartenStore" (fun _ -> martenStore |> createNuget)
Target.create "NugetAll" (fun _ -> 
    [
        "NugetCosmoStore"
        "NugetTableStorage"
        "NugetCosmosDb"
        "NugetMartenStore"
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

// start build
Fake.Core.Target.runOrDefaultWithArguments "BuildAll"
