module CosmoStore.Conversion

let eventWriteToEventRead streamId position createdUtc (x:EventWrite) = {
    Id = x.Id
    CorrelationId = x.CorrelationId
    CausationId = x.CausationId
    StreamId = streamId
    Position = position
    Name = x.Name
    Data = x.Data
    Metadata = x.Metadata
    CreatedUtc = createdUtc
}