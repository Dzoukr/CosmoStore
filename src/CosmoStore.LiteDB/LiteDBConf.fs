namespace CosmoStore.LiteDb

type StoreType = Memory | LocalDB
type DBMode = | Exclusive | ReadOnly | Shared

type Configuration = {
    StoreType : StoreType
    Name : string
    Folder : string
    DBMode : DBMode
} with
    static member Empty = {
        StoreType = Memory
        Name = "liteDBEvents"
        Folder = "AppData"
        DBMode = Exclusive
    }

module Store = 
    open System.IO
    open LiteDB.FSharp
    open LiteDB

    let (</>) x y = Path.Combine(x, y)

    (**
        Find datafolder and create if folder does not exist
        Change this logic if you required to put database file at specific location
    *)
    let private dataFolder dataFolderName =
        let appDataFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        let folder = appDataFolder </> dataFolderName
        let directoryInfo = DirectoryInfo(folder)
        if not directoryInfo.Exists then
            Directory.CreateDirectory folder |> ignore
        printfn "Using data folder: %s" folder
        folder

    let private databaseFilePath dataFolderName databaseName = dataFolder dataFolderName </> databaseName

    (**
        Create database store either in memory or file based.
        Dev is running in memory database while production will point to file based
    *)
    let createDatabaseUsing (conf: Configuration) =
        let mapper = FSharpBsonMapper()
        match conf.StoreType with
        | Memory ->
            let memoryStream = new System.IO.MemoryStream()
            new LiteDatabase(memoryStream, mapper)
        | LocalDB ->
            let connString = sprintf "Filename=%s;Mode=%s" (databaseFilePath conf.Folder conf.Name) (conf.DBMode.ToString())
            new LiteDatabase(connString, mapper)




