namespace CosmoStore.TableStorage

open System

type Configuration = {
    DatabaseName : string
    ServiceEndpoint : Uri
    AccountName : string
    AuthKey : string
}
with
    static member CreateDefault endpoint accountName authKey = {
        DatabaseName = "EventStore"
        ServiceEndpoint = endpoint
        AccountName = accountName
        AuthKey = authKey
    }