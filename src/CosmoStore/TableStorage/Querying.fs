module CosmoStore.TableStorage.Querying

open Microsoft.WindowsAzure.Storage.Table
open CosmoStore

let allStreams = TableQuery<DynamicTableEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, Conversion.streamRowKey))

let private allEventsFilter streamId =
    TableQuery.CombineFilters(
        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, streamId),
        TableOperators.And,
        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.NotEqual, Conversion.streamRowKey)
    )

let private toQuery filter = TableQuery<DynamicTableEntity>().Where(filter)

let private withPositionGreaterOrEqual pos filter =
    TableQuery.CombineFilters(
        filter,
        TableOperators.And,
        TableQuery.GenerateFilterConditionForLong("Position", QueryComparisons.GreaterThanOrEqual, pos)
    )
let private withPositionLessOrEqual pos filter =
    TableQuery.CombineFilters(
        filter,
        TableOperators.And,
        TableQuery.GenerateFilterConditionForLong("Position", QueryComparisons.LessThanOrEqual, pos)
    )

let allEventsFiltered streamId filter =
    let basicFilter = streamId |> allEventsFilter
    match filter with
    | AllEvents -> basicFilter |> toQuery
    | FromPosition p -> basicFilter |> withPositionGreaterOrEqual p |> toQuery
    | ToPosition p -> basicFilter |> withPositionLessOrEqual p |> toQuery
    | PositionRange(f,t) -> basicFilter |> withPositionGreaterOrEqual f |> withPositionLessOrEqual t |> toQuery