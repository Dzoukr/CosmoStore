module CosmoStore.TableStorage.Conversion

open System
open Azure.Data.Tables
open CosmoStore
open System.Collections.Generic

let internal streamRowKey = "Stream"

let private versionOrPosition (x:TableEntity) =
    let pos = x.GetInt64("Position")
    if pos.HasValue then pos.Value
    else x.GetInt64("Version").Value

let entityToStream (x:TableEntity) = {
    Id = x.PartitionKey
    LastUpdatedUtc = x.Timestamp.Value.UtcDateTime
    LastVersion = x |> versionOrPosition
}

let updateStreamEntity lastVersion (x:TableEntity) =
    x.["Version"] <- lastVersion |> Nullable
    x

let eventWriteToEntity streamId version (x:EventWrite<_>) : TableEntity = 
    let entity = TableEntity(streamId, x.Id.ToString())
    entity.Add("Version", version |> Nullable)
    entity.Add("Name", x.Name)
    
    match x.CorrelationId with
    | Some corrId -> entity.Add("CorrelationId", corrId |> Nullable)
    | None -> ()
    
    match x.CausationId with
    | Some causId -> entity.Add("CausationId", causId |> Nullable)
    | None -> ()
    
    entity.Add("Data", x.Data |> Serialization.stringFromJToken)
    match x.Metadata with
    | Some meta ->
        entity.Add("Metadata", meta |> Serialization.stringFromJToken)
    | None -> ()
    entity

let isEvent (x:TableEntity) = x.RowKey <> streamRowKey

let newStreamEntity streamId = TableEntity(streamId, "Stream")

let entityToEventRead (x:TableEntity) : EventRead<_,_> =
    {
        Id = x.RowKey |> Guid
        CorrelationId = x.GetGuid "CorrelationId" |> Option.ofNullable
        CausationId = x.GetGuid "CausationId" |> Option.ofNullable
        StreamId = x.PartitionKey
        Version = x |> versionOrPosition
        Name = x.GetString("Name")
        Data = x.GetString("Data") |> Serialization.stringToJToken
        Metadata = x.GetString "Metadata" |> Option.ofObj |> Option.map Serialization.stringToJToken
        CreatedUtc = x.Timestamp.Value.UtcDateTime
    }