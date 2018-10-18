namespace CosmoStore.TableStorage

open System

type Configuration = {
    DatabaseName : string
    AccountName : string
    AuthKey : string
}
with
    static member CreateDefault accountName authKey = {
        DatabaseName = "EventStore"
        AccountName = accountName
        AuthKey = authKey
    }