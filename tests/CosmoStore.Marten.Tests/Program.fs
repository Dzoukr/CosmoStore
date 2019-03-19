// Learn more about F# at http://fsharp.org

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

let createConnString host user pass database =
    sprintf "Host=%s;Username=%s;Password=%s;Database=%s" host user pass database
    |> NpgsqlConnectionStringBuilder


type DisposableDatabase(superConn: NpgsqlConnectionStringBuilder, databaseName: string) =
    static member Create(connStr) =
        let databaseName = System.Guid.NewGuid().ToString("n")
        createDatabase (connStr |> string) databaseName

        new DisposableDatabase(connStr, databaseName)
    member x.SuperConn = superConn
    member x.Conn =
        let builder = x.SuperConn |> string |> NpgsqlConnectionStringBuilder
        builder.Database <- x.DatabaseName
        builder
    member x.DatabaseName = databaseName
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
let superUserConnStr() = createConnString (host()) (user()) (pass()) (db())

let getNewDatabase() =
    let rec inner() =
        try
            superUserConnStr() |> DisposableDatabase.Create
        with e ->
            inner()
    inner()

let getStore (database: DisposableDatabase) = database.Conn |> string |> DocumentStore.For


let testConfig =
    { Expecto.Tests.defaultConfig with
        parallelWorkers = 2
        verbosity = LogLevel.Debug }

let withDatabase f () =
    use database = getNewDatabase()
    f database

let private getCleanEventStore() =
    let database = getNewDatabase()
    let store = getStore database 
    getEventStore ({ MartenStore = store })

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
        (cfg(), "Marten")
        |> AllTests.getTests
        |> runTests testConfig
    with
        exn -> printfn "%A" exn |> fun _ -> 0