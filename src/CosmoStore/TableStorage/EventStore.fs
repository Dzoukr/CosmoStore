module CosmoStore.TableStorage.EventStore

open System
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open CosmoStore
open FSharp.Control.Tasks.V2
open Microsoft.WindowsAzure.Storage

let private tableName = "Events"

let private tryGetStreamMetadata (table:CloudTable) (streamId:string) =
    task {
        let operation = TableOperation.Retrieve<DynamicTableEntity>(streamId, "Stream")
        let! r = table.ExecuteAsync(operation)
        match r.Result with
        | null -> return None
        | v -> 
            let entity = v :?> DynamicTableEntity
            return (entity, entity |> Conversion.entityToStream) |> Some
    }

let private validatePosition streamId (nextPos:int64) = function
    | ExpectedPosition.Any -> ()
    | ExpectedPosition.NoStream -> 
        if nextPos > 1L then 
            failwithf "Stream '%s' was expected to be empty, but contains %i events" streamId (nextPos - 1L)
    | ExpectedPosition.Exact expectedPos ->
        if nextPos <> expectedPos then
            failwithf "Stream '%s' was expected to have next position %i, but has %i" streamId expectedPos nextPos


let appendEvents (client:CloudTableClient) (streamId:string) (expectedPosition:ExpectedPosition) (events:EventWrite list) =
    let table = client.GetTableReference(tableName)
    
    task {
        
        let! lastPosition, metadataEntity = 
            task {
                let! str = streamId |> tryGetStreamMetadata table
                match str with
                | Some (entity, metadata) ->
                    return metadata.LastPosition, (Some entity)
                | None -> return 0L, None
            }

        let nextPos = lastPosition + 1L        
        do validatePosition streamId nextPos expectedPosition

        let batchOperation = TableBatchOperation()

        let ops = 
            events
            |> List.mapi (fun i evn -> 
                evn |> Conversion.eventWriteToEntity streamId (nextPos + (int64 i))
            )
        
        // insert or update metadata
        match metadataEntity with
        | Some e ->
            e
            |> Conversion.updateStreamEntity (lastPosition + (int64 events.Length))
            |> batchOperation.Replace
        | None -> 
            let e = DynamicTableEntity(streamId, "Stream")
            e
            |> Conversion.updateStreamEntity (int64 events.Length)
            |> batchOperation.Insert

        // insert events in batch
        ops |> List.iter batchOperation.Insert
        
        let! results = table.ExecuteBatchAsync(batchOperation)
        return results 
        |> Seq.map (fun x -> x.Result :?> DynamicTableEntity |> Conversion.entityToEventRead)
        |> Seq.toList
        |> List.sortBy (fun x -> x.Position)
    }

let appendEvent (client:CloudTableClient) (streamId:string) (expectedPosition:ExpectedPosition) (event:EventWrite) =
    task {
        let! events = event |> List.singleton |> appendEvents client streamId expectedPosition
        return events.Head
    }

let rec private executeQuery (table:CloudTable) (query:TableQuery<_>) (token:TableContinuationToken) (values:Collections.Generic.List<_>) =
    match token with
    | null -> task { return values }
    | t -> 
        task {
            let! res = table.ExecuteQuerySegmentedAsync(query, t)
            do values.AddRange(res.Results)
            return! executeQuery table query res.ContinuationToken values
        }

let private getStreams (client:CloudTableClient) (streamsRead:StreamsReadFilter) =
    let table = client.GetTableReference(tableName)
    let q = TableQuery<DynamicTableEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, "Stream"))
    let byReadFilter (s:Stream) =
        match streamsRead with
        | StreamsReadFilter.AllStreams -> true
        | StreamsReadFilter.Contains c -> s.Id.Contains(c)
        | StreamsReadFilter.EndsWith c -> s.Id.EndsWith(c)
        | StreamsReadFilter.StarsWith c -> s.Id.StartsWith(c)


    task {
        let token = TableContinuationToken()
        let! results = executeQuery table q token (Collections.Generic.List())
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToStream
            |> List.filter byReadFilter
            |> List.sortBy (fun x -> x.Id)
    }


let private getEvents (client:CloudTableClient) streamId (eventsRead:EventsReadRange) =
    let table = client.GetTableReference(tableName)
    let basicQuery = 
            TableQuery<DynamicTableEntity>()
                .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, streamId))
                .Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.NotEqual, "Stream"))
    let q =
        match eventsRead with
        | AllEvents -> basicQuery
        | FromPosition p -> basicQuery.Where(TableQuery.GenerateFilterCondition("Position", QueryComparisons.GreaterThanOrEqual, (p.ToString())))
        | ToPosition p -> basicQuery.Where(TableQuery.GenerateFilterCondition("Position", QueryComparisons.LessThanOrEqual, (p.ToString())))
        | PositionRange(f,t) ->
            basicQuery
                .Where(TableQuery.GenerateFilterCondition("Position", QueryComparisons.GreaterThanOrEqual, (f.ToString())))
                .Where(TableQuery.GenerateFilterCondition("Position", QueryComparisons.LessThanOrEqual, (t.ToString())))
    task {
        let token = TableContinuationToken()
        let! results = executeQuery table q token (Collections.Generic.List())
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToEventRead
            |> List.sortBy (fun x -> x.Position)
    }

let private getEvent (client:CloudTableClient) streamId position =
    task {
        let filter = EventsReadRange.PositionRange(position, position)
        let! events = getEvents client streamId filter
        return events.Head
    }

let getEventStore (configuration:Configuration) = 
    let credentials = Auth.StorageCredentials(configuration.AccountName, configuration.AuthKey)
    let uri = StorageUri(configuration.ServiceEndpoint)
    let account = CloudStorageAccount(credentials, uri, uri, uri, uri)
    let client = account.CreateCloudTableClient()
    client.GetTableReference("Events").CreateIfNotExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    {
        AppendEvent = appendEvent client
        AppendEvents = appendEvents client
        GetEvent = getEvent client
        GetEvents = getEvents client
        GetStreams = getStreams client
    }