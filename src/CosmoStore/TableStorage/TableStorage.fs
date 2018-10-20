namespace CosmoStore.TableStorage

type StorageAccount =
    | Cloud of accountName:string * authKey:string
    | LocalEmulator

type Configuration = {
    DatabaseName : string
    Account : StorageAccount
}
with
    static member CreateDefault accountName authKey = {
        DatabaseName = "EventStore"
        Account = Cloud(accountName, authKey)
    }
    static member CreateDefaultForLocalEmulator () = {
        DatabaseName = "EventStore"
        Account = LocalEmulator
    }
