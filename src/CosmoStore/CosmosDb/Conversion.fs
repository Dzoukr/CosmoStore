module CosmoStore.CosmosDb.Conversion

open CosmoStore

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