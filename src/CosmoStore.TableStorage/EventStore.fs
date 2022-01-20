module CosmoStore.TableStorage.EventStore

open System
open Azure.Data.Tables
open Azure.Data.Tables.Models
open CosmoStore
open FSharp.Control.Tasks.V2
open CosmoStore.TableStorage
open System.Collections.Generic
open System.Reactive.Linq
open System.Reactive.Concurrency

let private tryGetStreamMetadata (table:TableClient) (streamId:string) =
    task {
        let! r = table.GetEntityAsync(streamId, Conversion.streamRowKey)
        match r.Value with
        | null -> return None
        | entity ->
            return (entity, entity |> Conversion.entityToStream) |> Some
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
        
        let! response = table.SubmitTransactionAsync(batchOperation)
        let transactionResult = response.Value :?> TableTransactionResult
        return
            ops |> Seq.map (fun entity ->
                let r = transactionResult.GetResponseForEntity(entity.RowKey)
                if r.Status <= 299 && r.Status >= 200 then Some entity
                else None // Maybe we can ignore these since the transaction would have thrown?
            )
        |> Seq.choose id
        |> Seq.filter Conversion.isEvent
        |> Seq.map Conversion.entityToEventRead
        |> Seq.toList
        |> List.sortBy (fun x -> x.Version)
    }

(* // rework to linq?
let rec private executeQuery (table:CloudTable) (query:TableQuery<_>) (token:TableContinuationToken) (values:Collections.Generic.List<_>) =
    task {
        let! res = table.ExecuteQuerySegmentedAsync(query, token)
        do values.AddRange(res.Results)
        match res.ContinuationToken with
        | null -> return values
        | t -> return! executeQuery table query t values
    }
*)

let private getStreams (table:TableClient) (streamsRead:StreamsReadFilter) =
    let q = Querying.allStreams
    let byReadFilter (s:Stream<_>) =
        match streamsRead with
        | StreamsReadFilter.AllStreams -> true
        | StreamsReadFilter.Contains c -> s.Id.Contains(c)
        | StreamsReadFilter.EndsWith c -> s.Id.EndsWith(c)
        | StreamsReadFilter.StartsWith c -> s.Id.StartsWith(c)

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

let private getStream (table:CloudTable) streamId =
    let q = Querying.oneStream streamId
    task {
        let token = TableContinuationToken()
        let! results = executeQuery table q token (Collections.Generic.List())
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToStream
            |> List.head
    }

let private getEvents (table:CloudTable) streamId (eventsRead:EventsReadRange<_>) =
    let q = Querying.allEventsFiltered streamId eventsRead 
    task {
        let token = TableContinuationToken()
        let! results = executeQuery table q token (Collections.Generic.List())
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToEventRead
            |> List.sortBy (fun x -> x.Version)
    }

let private getEventsByCorrelationId (table:CloudTable) corrId  =
    let q = Querying.allEventsWithCorrelationIdFilter corrId 
    task {
        let token = TableContinuationToken()
        let! results = executeQuery table q token (Collections.Generic.List())
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToEventRead
            |> List.sortBy (fun x -> x.CreatedUtc)
    }

let private getEvent (table:CloudTable) streamId version =
    task {
        let filter = EventsReadRange.VersionRange(version, version)
        let! events = getEvents table streamId filter
        return events.Head
    }

let getEventStore (configuration:Configuration) = 
    let account = 
        match configuration.Account with
        | Cloud (accountName, authKey) -> 
            let credentials = Auth.StorageCredentials(accountName, authKey)
            CloudStorageAccount(credentials, true)
        | CloudBySAS (accountName, sasToken) ->
            let credentials = Auth.StorageCredentials(sasToken)
            CloudStorageAccount(credentials, accountName, "core.windows.net", true )
        | SovereignCloud (accountName, authKey, endpointSuffix) ->
            let credentials = Auth.StorageCredentials(accountName, authKey)
            CloudStorageAccount(credentials, accountName, endpointSuffix, true )
        | SovereignCloudBySAS (accountName, sasToken, endpointSuffix) ->
            let credentials = Auth.StorageCredentials(sasToken)
            CloudStorageAccount(credentials, accountName, endpointSuffix, true )
        | LocalEmulator -> CloudStorageAccount.DevelopmentStorageAccount

    let eventAppended = Event<EventRead<_,_>>()
    let client = account.CreateCloudTableClient()
    client.GetTableReference(configuration.TableName).CreateIfNotExistsAsync() |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    
    let table = client.GetTableReference(configuration.TableName)
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