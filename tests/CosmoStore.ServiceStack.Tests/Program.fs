// Learn more about F# at http://fsharp.org

open ServiceStack
open ServiceStack.Text
open CosmoStore
open System
open System.IO
open Expecto
open Expecto.Logging
open CosmoStore.ServiceStack.EventStore
open CosmoStore.Tests
open CosmoStore.Tests.Domain
open ServiceStack.OrmLite
open CosmoStore.ServiceStack

[<CLIMutable>]
[<Struct>]
type Person =
    { FirstName: string }

let testConfig =
    { Expecto.Tests.defaultConfig with
          parallelWorkers = 2
          verbosity = LogLevel.Debug }

type DisposableDatabase private (databaseName: string) =

    static member Create() =
        let databaseName = System.Guid.NewGuid().ToString("n").MapAbsolutePath()
        new DisposableDatabase(databaseName)

    member x.Conf =
        { Factory = OrmLiteConnectionFactory(databaseName, SqliteDialect.Provider); Serializer = JsvStringSerializer() }

    interface IDisposable with
        member x.Dispose() =
            if (File.Exists(databaseName)) then File.Delete(databaseName)
            else ()

let getNewDatabase() =
    let rec inner() =
        try
            DisposableDatabase.Create()
        with e ->
            inner()
    inner()

let mutable databases: DisposableDatabase list = []

let private getCleanEventStore() =
    let database = getNewDatabase()
    databases <- database :: databases
    getEventStore database.Conf


let private getEvent (i: int): EventWrite<_> =
    let corr, caus =
        match i % 2, i % 3 with
        | 0, _ -> (Some <| Guid.NewGuid()), None
        | _, 0 -> None, (Some <| Guid.NewGuid())
        | _ -> None, None

    { Id = Guid.NewGuid()
      CorrelationId = corr
      CausationId = caus
      Name = sprintf "Created_%i" i
      Data = { FirstName = "Some cool data, right?" }
      Metadata = None }

let ServiceStackGenerator: TestDataGenerator<_> =
    { GetStreamId = fun _ -> sprintf "TestStream_%A" (Guid.NewGuid())
      GetEvent = getEvent }

[<EntryPoint>]
let main _ =
    try
        try
            AllTests.getTests "ServiceStack" ServiceStackGenerator (getCleanEventStore()) |> runTests testConfig
        with exn -> printfn "%A" exn |> fun _ -> 0
    finally
        databases
        |> List.map (fun x -> (x :> IDisposable).Dispose())
        |> ignore
