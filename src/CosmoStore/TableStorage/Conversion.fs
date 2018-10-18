module CosmoStore.TableStorage.Conversion

open System
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open CosmoStore
open Microsoft.WindowsAzure.Storage.Table
open Microsoft.WindowsAzure.Storage.Table
open System
open Microsoft.WindowsAzure.Storage.Table
open Microsoft.WindowsAzure.Storage.Table
open Microsoft.WindowsAzure.Storage.Table

let entityToStream (x:DynamicTableEntity) = {
    Id = x.PartitionKey
    LastUpdatedUtc = x.Properties.["LastUpdatedUtc"].DateTime |> fun x -> x.Value
    LastPosition = x.Properties.["LastPosition"].Int64Value |> fun x -> x.Value
}

let updateStreamEntity lastPosition (x:DynamicTableEntity) =
    x.Properties.["LastUpdatedUtc"] <- EntityProperty.CreateEntityPropertyFromObject DateTime.UtcNow
    x.Properties.["LastPosition"] <- EntityProperty.GeneratePropertyForLong(lastPosition |> Nullable)
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