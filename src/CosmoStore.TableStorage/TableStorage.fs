namespace CosmoStore.TableStorage
open System

type StorageAccount =
    | Cloud of accountName:string * authKey:string
    | LocalEmulator

type Configuration = {
    TableName : string
    Account : StorageAccount
}
with
    static member CreateDefault accountName authKey = {
        TableName = "Events"
        Account = Cloud(accountName, authKey)
    }
    static member CreateDefaultForLocalEmulator () = {
        TableName = "Events"
        Account = LocalEmulator
    }