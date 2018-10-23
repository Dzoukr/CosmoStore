#r "paket: groupref Build //"

#load ".fake/build.fsx/intellisense.fsx"
open Fake
open Fake.Core
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.DotNet.Testing

let appSrc = "src/CosmoStore"
let testsSrc = "tests/CosmoStore.Tests"

let package = "CosmoStore"
let github = package
let tags = "FSharp CosmosDB DocumentDB EventSourcing EventStore Azure TableStorage"
let description = "F# Event store for Azure Cosmos DB and Azure Table Storage"

Target.create "Build" (fun _ ->
    appSrc |> DotNet.build id
)

Target.create "RunTests" (fun _ ->
    testsSrc |> DotNet.test id
)

Fake.Core.Target.create "Clean" (fun _ -> 
    !! "src/*/bin"
    ++ "src/*/obj"
    ++ "tests/*/bin"
    ++ "tests/*/obj"
    |> Shell.deleteDirs
)

// Read release notes & version info from RELEASE_NOTES.md
let release = ReleaseNotes.load "RELEASE_NOTES.md"

Target.create "Nuget" (fun _ ->
    let toNotes = List.map (fun x -> x + System.Environment.NewLine) >> List.fold (+) ""
    let args = 
        [
            sprintf "PackageId=\"%s\"" package
            sprintf "Title=\"%s\"" package
            sprintf "Description=\"%s\"" description
            sprintf "Summary=\"%s\"" description
            sprintf "PackageVersion=\"%s\"" release.NugetVersion
            sprintf "PackageReleaseNotes=\"%s\"" (release.Notes |> toNotes)
            sprintf "PackageLicenseUrl=\"http://github.com/dzoukr/%s/blob/master/LICENSE.md\"" github
            sprintf "PackageProjectUrl=\"http://github.com/dzoukr/%s\"" github
            "PackageIconUrl=\"\""
            "PackageIconUrl=\"https://raw.githubusercontent.com/Dzoukr/CosmoStore/master/logo.png\""
            sprintf "PackageTags=\"%s\"" tags
            "Copyright=\"Roman Provazník - 2018\""
            "Authors=\"Roman Provazník\""
        ] 
        |> List.map (fun x -> "/p:" + x)
        |> String.concat " "

    
    appSrc |> DotNet.pack (fun p -> { p with Configuration = DotNet.Custom "Release"; OutputPath = Some "../../nuget"; Common = { p.Common with CustomParams = Some args } })
)

"Clean" ==> "Build"
"RunTests" ==> "Clean" ==> "Nuget"

// start build
Fake.Core.Target.runOrDefault "Build"