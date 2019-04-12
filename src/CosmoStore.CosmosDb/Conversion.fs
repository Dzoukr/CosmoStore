module CosmoStore.CosmosDb.Conversion

open System
open CosmoStore
open Microsoft.Azure.Documents
open Newtonsoft.Json.Linq

let documentToStream (x:Document) = {
    Id = x.GetPropertyValue<string>("streamId")
    LastUpdatedUtc = x.GetPropertyValue<DateTime>("lastUpdatedUtc")
    LastPosition = x.GetPropertyValue<int64>("lastPosition")
}

let documentToEventRead (doc:Document) = 
    { 
        Id = Guid(doc.Id)
        CorrelationId = doc.GetPropertyValue<string>("correlationId") |> function | null -> None | x -> Some (Guid x)
        CausationId = doc.GetPropertyValue<string>("causationId") |> function | null -> None | x -> Some (Guid x)
        StreamId = doc.GetPropertyValue<string>("streamId")
        Position = doc.GetPropertyValue<int64>("position")
        Name = doc.GetPropertyValue<string>("name")
        Data = doc.GetPropertyValue<JToken>("data")
        Metadata = doc.GetPropertyValue<JToken>("metadata") |> function | null -> None | x -> Some x
        CreatedUtc = doc.GetPropertyValue<DateTime>("createdUtc")
    }
