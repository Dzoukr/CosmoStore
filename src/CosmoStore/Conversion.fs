module CosmoStore.Conversion

let eventWriteToEventRead streamId version createdUtc (x:EventWrite<_>) = {
    Id = x.Id
    CorrelationId = x.CorrelationId
    CausationId = x.CausationId
    StreamId = streamId
    Version = version
    Name = x.Name
    Data = x.Data
    Metadata = x.Metadata
    CreatedUtc = createdUtc
}