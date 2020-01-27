#r "paket: groupref Build //"
#load ".fake/build.fsx/intellisense.fsx"

open System.IO
open Fake.IO
open Fake.Core
open Fake.DotNet
open Fake.IO.FileSystemOperators
open Fake.Core.TargetOperators

module Tools =
    let private findTool tool winTool =
        let tool = if Environment.isUnix then tool else winTool
        match ProcessUtils.tryFindFileOnPath tool with
        | Some t -> t
        | _ ->
            let errorMsg =
                tool + " was not found in path. " +
                "Please install it and make sure it's available from your path. "
            failwith errorMsg
            
    let private runTool (cmd:string) args workingDir =
        let arguments = args |> String.split ' ' |> Arguments.OfArgs
        Command.RawCommand (cmd, arguments)
        |> CreateProcess.fromCommand
        |> CreateProcess.withWorkingDirectory workingDir
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore
        
    let dotnet cmd workingDir =
        let result =
            DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
        if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir
    

// Targets
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

"TestCosmoStore" ==> "PackCosmoStore"
"TestCosmoStore" ==> "PublishCosmoStore"

Target.create "PackTableStorage" (fun _ -> "src" </> "CosmoStore.TableStorage" |> createNuget)
Target.create "PublishTableStorage" (fun _ -> "src" </> "CosmoStore.TableStorage" |> publishNuget)
Target.create "TestTableStorage" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.TableStorage.Tests"))

"TestTableStorage" ==> "PackTableStorage"
"TestTableStorage" ==> "PublishTableStorage"

Target.create "PackCosmosDb" (fun _ -> "src" </> "CosmoStore.CosmosDb" |> createNuget)
Target.create "PublishCosmosDb" (fun _ -> "src" </> "CosmoStore.CosmosDb" |> publishNuget)
Target.create "TestCosmosDb" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.CosmosDb.Tests"))

"TestCosmosDb" ==> "PackCosmosDb"
"TestCosmosDb" ==> "PublishCosmosDb"

Target.create "PackMarten" (fun _ -> "src" </> "CosmoStore.Marten" |> createNuget)
Target.create "PublishMarten" (fun _ -> "src" </> "CosmoStore.Marten" |> publishNuget)
Target.create "TestMarten" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.Marten.Tests"))

"TestMarten" ==> "PackMarten"
"TestMarten" ==> "PublishMarten"

Target.create "PackInMemory" (fun _ -> "src" </> "CosmoStore.InMemory" |> createNuget)
Target.create "PublishInMemory" (fun _ -> "src" </> "CosmoStore.InMemory" |> publishNuget)
Target.create "TestInMemory" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.InMemory.Tests"))

"TestInMemory" ==> "PackInMemory"
"TestInMemory" ==> "PublishInMemory"

Target.create "PackLiteDb" (fun _ -> "src" </> "CosmoStore.LiteDb" |> createNuget)
Target.create "PublishLiteDb" (fun _ -> "src" </> "CosmoStore.LiteDb" |> publishNuget)
Target.create "TestLiteDb" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.LiteDb.Tests"))

"TestLiteDb" ==> "PackLiteDb"
"TestLiteDb" ==> "PublishLiteDb"

Target.create "PackServiceStack" (fun _ -> "src" </> "CosmoStore.ServiceStack" |> createNuget)
Target.create "PublishServiceStack" (fun _ -> "src" </> "CosmoStore.ServiceStack" |> publishNuget)
Target.create "TestServiceStack" (fun _ -> Tools.dotnet "run" ("tests" </> "CosmoStore.ServiceStack.Tests"))

"TestServiceStack" ==> "PackServiceStack"
"TestServiceStack" ==> "PublishServiceStack"

Target.runOrDefaultWithArguments "TestCosmoStore"