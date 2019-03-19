// Learn more about F# at http://fsharp.org

open CosmoStore.Marten
open CosmoStore.Marten
open CosmoStore.Tests
open Expecto
open Expecto.Logging
open Marten
open System
open Npgsql
open CosmoStore.Marten.EventStore



let execNonQuery connStr commandStr =
        use conn = new NpgsqlConnection(connStr)
        use cmd = new NpgsqlCommand(commandStr, conn)
        conn.Open()
        cmd.ExecuteNonQuery()

let createDatabase connStr databaseName =
    databaseName
    |> sprintf "CREATE database \"%s\" ENCODING = 'UTF8'"
    |> execNonQuery connStr
    |> ignore
    

let dropDatabase connStr databaseName =
    //kill out all connections
    databaseName
    |> sprintf "select pg_terminate_backend(pid) from pg_stat_activity where datname='%s';"
    |> execNonQuery connStr
    |> ignore

    databaseName
    |> sprintf "DROP database \"%s\""
    |> execNonQuery connStr
    |> ignore




type DisposableDatabase private (superConn: NpgsqlConnectionStringBuilder, databaseName: string, conf : Configuration) =
    static member Create(conf) =
        let databaseName = System.Guid.NewGuid().ToString("n")
        let connStr = userConnStr conf
        createDatabase (connStr |> string) databaseName
        new DisposableDatabase(connStr, databaseName, conf)

    member x.Conf = {conf with Database = databaseName }
    
    interface IDisposable with
        member x.Dispose() =
            dropDatabase (superConn |> string) databaseName

let getEnvOrDefault defaultVal str =
    let envVar = System.Environment.GetEnvironmentVariable str
    if String.IsNullOrEmpty envVar then defaultVal
    else envVar


let host() = "POSTGRES_HOST" |> getEnvOrDefault "localhost"
let user() = "POSTGRES_USER" |> getEnvOrDefault "postgres"
let pass() = "POSTGRES_PASS" |> getEnvOrDefault "postgres"
let db() = "POSTGRES_DB" |> getEnvOrDefault "postgres"

let getNewDatabase(conf) =
    let rec inner() =
        try
            conf |> DisposableDatabase.Create
        with e ->
            inner()
    inner()

let testConfig =
    { Expecto.Tests.defaultConfig with
        parallelWorkers = 2
        verbosity = LogLevel.Debug }
let mutable databases : DisposableDatabase list = [] //mutable list to dispose all the database

let private getCleanEventStore () =
    let conf = {Host = host(); Username = user(); Password = pass(); Database = db()}
    let database = getNewDatabase(conf)
    databases <- database :: databases
    getEventStore database.Conf

let cfg() = Domain.defaultTestConfiguration getCleanEventStore

type Person = {
    Id : int
    Name : string
}

let tests =
  test "A simple test" {
    let subject = "Hello World"
    Expect.equal subject "Hello World" "The strings should equal"
  }


[<EntryPoint>]
let main _ =
    try 
        try 
            (cfg(), "Marten")
            |> AllTests.getTests
            |> runTests testConfig
        with
            exn -> printfn "%A" exn |> fun _ -> 0
    finally
        databases |> List.map(fun x -> (x :> IDisposable).Dispose()) |> ignore
    