module CosmoStore.TableStorage.Querying

open Microsoft.WindowsAzure.Storage.Table
open CosmoStore

let private toQuery filter = TableQuery<DynamicTableEntity>().Where(filter)

let allStreams = TableQuery<DynamicTableEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, Conversion.streamRowKey))

let oneStream streamId = 
    TableQuery.CombineFilters(
        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, streamId),
        TableOperators.And,
        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, Conversion.streamRowKey)
    ) |> toQuery

let allEventsWithCorrelationIdFilter corrId =
    TableQuery.GenerateFilterConditionForGuid("CorrelationId", QueryComparisons.Equal, corrId)
    |> toQuery

let private allEventsFilter streamId =
    TableQuery.CombineFilters(
        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, streamId),
        TableOperators.And,
        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.NotEqual, Conversion.streamRowKey)
    )

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
    | FromVersion p -> basicFilter |> withPositionGreaterOrEqual p |> toQuery
    | ToVersion p -> basicFilter |> withPositionLessOrEqual p |> toQuery
    | PositionRange(f,t) -> basicFilter |> withPositionGreaterOrEqual f |> withPositionLessOrEqual t |> toQuery