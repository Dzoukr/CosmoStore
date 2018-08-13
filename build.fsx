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

Target.create "Build" (fun _ ->
    appSrc |> DotNet.build id
)

Fake.Core.Target.create "Clean" (fun _ -> 
    !! "src/*/bin"
    ++ "src/*/obj"
    ++ "tests/*/bin"
    ++ "tests/*/obj"
    |> Shell.deleteDirs
)

"Clean" ==> "Build"

// start build
Fake.Core.Target.runOrDefault "Build"