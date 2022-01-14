module CosmoStore.TableStorage.Querying

open Azure.Data.Tables
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

let private versionOrPositionFilter qc ver =
        TableQuery.CombineFilters(
            TableQuery.GenerateFilterConditionForLong("Version", qc, ver),
            TableOperators.Or,
            TableQuery.GenerateFilterConditionForLong("Position", qc, ver)
        )

let private withVersionGreaterOrEqual ver filter =
    TableQuery.CombineFilters(
        filter,
        TableOperators.And,
        versionOrPositionFilter QueryComparisons.GreaterThanOrEqual ver
    )
let private withVersionLessOrEqual ver filter =
    TableQuery.CombineFilters(
        filter,
        TableOperators.And,
        versionOrPositionFilter QueryComparisons.LessThanOrEqual ver
    )

let allEventsFiltered streamId filter =
    let basicFilter = streamId |> allEventsFilter
    match filter with
    | AllEvents -> basicFilter |> toQuery
    | FromVersion p -> basicFilter |> withVersionGreaterOrEqual p |> toQuery
    | ToVersion p -> basicFilter |> withVersionLessOrEqual p |> toQuery
    | VersionRange(f,t) -> basicFilter |> withVersionGreaterOrEqual f |> withVersionLessOrEqual t |> toQuery