module CosmoStore.TableStorage.EventStore

open System
open Azure.Data.Tables
open CosmoStore
open FSharp.Control.Tasks.V2
open FSharp.Control
open CosmoStore.TableStorage
open System.Collections.Generic
open System.Reactive.Linq
open System.Reactive.Concurrency

let private tryGetStreamMetadata (table:TableClient) (streamId:string) =
    task {
        try
            let! r = table.GetEntityAsync(streamId, Conversion.streamRowKey)
            return r.Value
            |> Option.ofObj
            |> Option.map
                (fun entity ->
                    (entity, entity |> Conversion.entityToStream))
        with :? Azure.RequestFailedException -> return None
    }

let private appendEvents (table:TableClient) (streamId:string) (expectedVersion:ExpectedVersion<_>) (events:EventWrite<_> list) =
    task {
        let! lastVersion, metadataEntity = 
            task {
                let! str = streamId |> tryGetStreamMetadata table
                match str with
                | Some (entity, metadata) ->
                    return metadata.LastVersion, (Some entity)
                | None -> return 0L, None
            }

        let nextPos = lastVersion + 1L        
        do Validation.validateVersion streamId nextPos expectedVersion

        let batchOperation = List<TableTransactionAction>()

        let ops = 
            events
            |> List.mapi (fun i evn -> 
                evn |> Conversion.eventWriteToEntity streamId (nextPos + (int64 i))
            )
        
        // insert or update metadata
        match metadataEntity with
        | Some e ->
            e
            |> Conversion.updateStreamEntity (lastVersion + (int64 events.Length))
            |> fun entity -> TableTransactionAction(TableTransactionActionType.UpsertReplace, entity)
            |> batchOperation.Add
        | None -> 
            streamId
            |> Conversion.newStreamEntity
            |> Conversion.updateStreamEntity (int64 events.Length)
            |> fun entity -> TableTransactionAction(TableTransactionActionType.Add, entity)
            |> batchOperation.Add

        // insert events in batch
        ops |> Seq.map (fun entity -> TableTransactionAction(TableTransactionActionType.Add, entity)) |> batchOperation.AddRange
        try
            let! _ = table.SubmitTransactionAsync(batchOperation) // Throws on transaction failure.
            return ops
            |> Seq.filter Conversion.isEvent
            |> Seq.map Conversion.entityToEventRead
            |> Seq.toList
            |> List.sortBy (fun x -> x.Version)
        with :? TableTransactionFailedException as ex ->
            let failureMsg = 
                if ex.FailedTransactionActionIndex.HasValue then
                    let failedAction = batchOperation.[ex.FailedTransactionActionIndex.Value]
                    let failedEntity = failedAction.Entity
                    sprintf "Transaction failure '%s' for PartitionKey: '%s' and RowKey: '%s'" ex.Message failedEntity.PartitionKey failedEntity.RowKey
                else
                    sprintf "Transaction failure '%s'" ex.Message
            return raise (Exception (failureMsg, ex))
    }

let private executeQuery (table:TableClient) (query:string) =
    task {
        let values = List<_> ()
        let pages = table.QueryAsync(query).AsPages()
        let mutable pagesLeftToGet = true
        let e = pages.GetAsyncEnumerator()
        while pagesLeftToGet do
            if not(isNull e.Current) then
                values.AddRange(e.Current.Values)
            let! morePages = e.MoveNextAsync()
            pagesLeftToGet <- morePages
        return values
    }

let private getStreams (table:TableClient) (streamsRead:StreamsReadFilter) =
    let q = Querying.allStreams
    let byReadFilter (s:Stream<_>) =
        match streamsRead with
        | StreamsReadFilter.AllStreams -> true
        | StreamsReadFilter.Contains c -> s.Id.Contains(c)
        | StreamsReadFilter.EndsWith c -> s.Id.EndsWith(c)
        | StreamsReadFilter.StartsWith c -> s.Id.StartsWith(c)

    task {
        let! results = executeQuery table q
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToStream
            |> List.filter byReadFilter
            |> List.sortBy (fun x -> x.Id)
    }

let private getStream (table:TableClient) streamId =
    let q = Querying.oneStream streamId
    task {
        let! results = executeQuery table q
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToStream
            |> List.head
    }

let private getEvents (table:TableClient) streamId (eventsRead:EventsReadRange<_>) =
    let q = Querying.allEventsFiltered streamId eventsRead 
    task {
        let! results = executeQuery table q
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToEventRead
            |> List.sortBy (fun x -> x.Version)
    }

let private getEventsByCorrelationId (table:TableClient) corrId  =
    let q = Querying.allEventsWithCorrelationIdFilter corrId 
    task {
        let! results = executeQuery table q
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToEventRead
            |> List.sortBy (fun x -> x.CreatedUtc)
    }

let private getEvent (table:TableClient) streamId version =
    task {
        let filter = EventsReadRange.VersionRange(version, version)
        let! events = getEvents table streamId filter
        return events.Head
    }

let getEventStore (configuration:Configuration) = 
    let defaultEndpointSuffix = "core.windows.net"
    let tableServiceClient = 
        match configuration.Account with
        | Cloud (accountName, authKey) ->
            TableServiceClient(
                endpoint = Uri (sprintf "https://%s.table.%s" accountName defaultEndpointSuffix),
                credential = TableSharedKeyCredential (accountName, authKey)
            )
        | CloudBySAS (accountName, sasToken) ->
            TableServiceClient(
                endpoint = Uri (sprintf "https://%s.table.%s" accountName defaultEndpointSuffix),
                credential = Azure.AzureSasCredential (sasToken)
            )
        | SovereignCloud (accountName, authKey, endpointSuffix) ->
            TableServiceClient(
                endpoint = Uri (sprintf "https://%s.table.%s" accountName endpointSuffix),
                credential = TableSharedKeyCredential (accountName, authKey)
            )
        | SovereignCloudBySAS (accountName, sasToken, endpointSuffix) ->
            TableServiceClient(
                endpoint = Uri (sprintf "https://%s.table.%s" accountName endpointSuffix),
                credential = Azure.AzureSasCredential (sasToken)
            )
        | LocalEmulator ->
            TableServiceClient ("DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;")

    let eventAppended = Event<EventRead<_,_>>()
    tableServiceClient.GetTableClient(configuration.TableName).CreateIfNotExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    
    let table = tableServiceClient.GetTableClient(configuration.TableName)
    {
        AppendEvent = fun stream pos event -> task {
            let! events = appendEvents table stream pos [event]
            events |> List.iter eventAppended.Trigger
            return events |> List.head
        }
        AppendEvents = fun stream pos events -> task {
            if events |> List.isEmpty then return []
            else 
                let! events = appendEvents table stream pos events
                events |> List.iter eventAppended.Trigger
                return events
        }
        GetEvent = getEvent table
        GetEvents = getEvents table
        GetEventsByCorrelationId = getEventsByCorrelationId table
        GetStreams = getStreams table
        GetStream = getStream table
        EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
    }