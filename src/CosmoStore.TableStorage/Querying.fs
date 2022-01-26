module CosmoStore.TableStorage.Querying

open CosmoStore
open Azure.Data.Tables.FSharp

let allStreams = Column ("RowKey", Eq Conversion.streamRowKey) |> FilterConverter.toQuery

let oneStream (streamId:string) =
    Binary (
        Column ("PartitionKey", Eq streamId),
        And,
        Column ("RowKey", Eq Conversion.streamRowKey)
    ) |> FilterConverter.toQuery

let allEventsWithCorrelationIdFilter (corrId:System.Guid) =
    Column ("CorrelationId", Eq corrId)
    |> FilterConverter.toQuery

let private allEventsFilter (streamId:string) =
    Binary (
        Column ("PartitionKey", Eq streamId),
        And,
        Column ("RowKey", Ne Conversion.streamRowKey)
    )

let private versionOrPositionFilter (qc:ColumnComparison) =
    Binary(
        Column ("Version", qc),
        Or,
        Column ("Position", qc)
    )

let private withVersionGreaterOrEqual ver filter =
    Binary(
        filter,
        And,
        versionOrPositionFilter (Ge ver)
    )

let private withVersionLessOrEqual ver filter =
    Binary(
        filter,
        And,
        versionOrPositionFilter (Le ver)
    )

let allEventsFiltered streamId filter =
    let basicFilter = streamId |> allEventsFilter
    match filter with
    | AllEvents -> basicFilter |> FilterConverter.toQuery
    | FromVersion p -> basicFilter |> withVersionGreaterOrEqual p |> FilterConverter.toQuery
    | ToVersion p -> basicFilter |> withVersionLessOrEqual p |> FilterConverter.toQuery
    | VersionRange(f,t) -> basicFilter |> withVersionGreaterOrEqual f |> withVersionLessOrEqual t |> FilterConverter.toQuery