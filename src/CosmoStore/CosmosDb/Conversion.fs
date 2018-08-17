module CosmoStore.CosmosDb.Conversion

open System
open CosmoStore
open Microsoft.Azure.Documents

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
    Id = x.GetPropertyValue<string>("refStreamId")
    LastUpdatedUtc = x.GetPropertyValue<DateTime>("lastUpdatedUtc")
    Position = x.GetPropertyValue<int64>("position")
}