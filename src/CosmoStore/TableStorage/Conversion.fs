module CosmoStore.TableStorage.Conversion

open System
open Microsoft.WindowsAzure.Storage.Table
open CosmoStore
open System.Collections.Generic

let internal streamRowKey = "Stream"

let entityToStream (x:DynamicTableEntity) = {
    Id = x.PartitionKey
    LastUpdatedUtc = x.Timestamp.UtcDateTime
    LastPosition = x.Properties.["Position"].Int64Value.Value
}

let updateStreamEntity lastPosition (x:DynamicTableEntity) =
    x.Properties.["Position"] <- EntityProperty.GeneratePropertyForLong(lastPosition |> Nullable)
    x

let eventWriteToEntity streamId position (x:EventWrite) : DynamicTableEntity = 
    let entity = DynamicTableEntity(streamId, x.Id.ToString())
    entity.Properties.Add("Position", EntityProperty.GeneratePropertyForLong(position |> Nullable))
    entity.Properties.Add("Name", EntityProperty.GeneratePropertyForString(x.Name))
    entity.Properties.Add("CorrelationId", EntityProperty.GeneratePropertyForGuid(x.CorrelationId |> Nullable))
    entity.Properties.Add("Data", EntityProperty.GeneratePropertyForString(x.Data |> Serialization.stringFromJToken))
    match x.Metadata with
    | Some meta ->
        entity.Properties.Add("Metadata", EntityProperty.GeneratePropertyForString(meta |> Serialization.stringFromJToken))
    | None -> ()
    entity

let private tryStringValue key (dict:IDictionary<string, EntityProperty>) = 
    match dict.TryGetValue(key) with
    | true, v ->
        match v with
        | null -> None
        | x -> x.StringValue |> Some
    | false, _ -> None
    
let isEvent (x:DynamicTableEntity) = x.RowKey <> streamRowKey

let newStreamEntity streamId = DynamicTableEntity(streamId, "Stream")

let entityToEventRead (x:DynamicTableEntity) : EventRead =
    {
        Id = x.RowKey |> Guid
        CorrelationId = x.Properties.["CorrelationId"].GuidValue.Value
        StreamId = x.PartitionKey
        Position = x.Properties.["Position"].Int64Value.Value
        Name = x.Properties.["Name"].StringValue
        Data = x.Properties.["Data"].StringValue |> Serialization.stringToJToken
        Metadata = x.Properties |> tryStringValue "Metadata" |> Option.map Serialization.stringToJToken
        CreatedUtc = x.Timestamp.UtcDateTime
    }