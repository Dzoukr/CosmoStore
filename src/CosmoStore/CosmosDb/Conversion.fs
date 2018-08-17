module CosmoStore.CosmosDb.Conversion

open System
open CosmoStore
open Microsoft.Azure.Documents
open Newtonsoft.Json.Linq

let eventWriteToEventRead streamId position createdUtc (x:EventWrite) = {
    Id = x.Id
    CorrelationId = x.CorrelationId
    StreamId = streamId
    Position = position
    Name = x.Name
    Data = x.Data
    Metadata = x.Metadata
    CreatedUtc = createdUtc
}

let documentToStream (x:Document) = {
    Id = x.GetPropertyValue<string>("streamId")
    LastUpdatedUtc = x.GetPropertyValue<DateTime>("lastUpdatedUtc")
    LastPosition = x.GetPropertyValue<int64>("lastPosition")
}

let documentToEventRead (doc:Document) = 
    { 
        Id = Guid(doc.Id)
        CorrelationId = doc.GetPropertyValue<Guid>("correlationId")
        StreamId = doc.GetPropertyValue<string>("streamId")
        Position = doc.GetPropertyValue<int64>("position")
        Name = doc.GetPropertyValue<string>("name")
        Data = doc.GetPropertyValue<JToken>("data")
        Metadata = doc.GetPropertyValue<JToken>("metadata") |> function | null -> None | x -> Some x
        CreatedUtc = doc.GetPropertyValue<DateTime>("createdUtc")
    }
