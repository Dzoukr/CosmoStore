module CosmoStore.TableStorage.EventStore

open System
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open CosmoStore
open FSharp.Control.Tasks.V2
open CosmoStore.TableStorage
open System.Reactive.Linq
open System.Reactive.Concurrency

let private tryGetStreamMetadata (table:CloudTable) (streamId:string) =
    task {
        let operation = TableOperation.Retrieve<DynamicTableEntity>(streamId, Conversion.streamRowKey)
        let! r = table.ExecuteAsync(operation)
        match r.Result with
        | null -> return None
        | v -> 
            let entity = v :?> DynamicTableEntity
            return (entity, entity |> Conversion.entityToStream) |> Some
    }


let private appendEvents (table:CloudTable) (streamId:string) (expectedPosition:ExpectedPosition) (events:EventWrite list) =
    
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
        do Validation.validatePosition streamId nextPos expectedPosition

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
            streamId
            |> Conversion.newStreamEntity
            |> Conversion.updateStreamEntity (int64 events.Length)
            |> batchOperation.Insert

        // insert events in batch
        ops |> List.iter batchOperation.Insert
        
        let! results = table.ExecuteBatchAsync(batchOperation)
        return results 
        |> Seq.map (fun x -> x.Result :?> DynamicTableEntity)
        |> Seq.filter Conversion.isEvent
        |> Seq.map Conversion.entityToEventRead
        |> Seq.toList
        |> List.sortBy (fun x -> x.Position)
    }

let rec private executeQuery (table:CloudTable) (query:TableQuery<_>) (token:TableContinuationToken) (values:Collections.Generic.List<_>) =
    task {
        let! res = table.ExecuteQuerySegmentedAsync(query, token)
        do values.AddRange(res.Results)
        match res.ContinuationToken with
        | null -> return values
        | t -> return! executeQuery table query t values
    }

let private getStreams (table:CloudTable) (streamsRead:StreamsReadFilter) =
    let q = Querying.allStreams
    let byReadFilter (s:Stream) =
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

let private getEvents (table:CloudTable) streamId (eventsRead:EventsReadRange) =
    let q = Querying.allEventsFiltered streamId eventsRead 
    task {
        let token = TableContinuationToken()
        let! results = executeQuery table q token (Collections.Generic.List())
        return 
            results
            |> Seq.toList
            |> List.map Conversion.entityToEventRead
            |> List.sortBy (fun x -> x.Position)
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

let private getEvent (table:CloudTable) streamId position =
    task {
        let filter = EventsReadRange.PositionRange(position, position)
        let! events = getEvents table streamId filter
        return events.Head
    }

let getEventStore (configuration:Configuration) = 
    let account = 
        match configuration.Account with
        | Cloud (accountName, authKey) -> 
            let credentials = Auth.StorageCredentials(accountName, authKey)
            CloudStorageAccount(credentials, true)
        | LocalEmulator -> CloudStorageAccount.DevelopmentStorageAccount

    let eventAppended = Event<EventRead>()
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