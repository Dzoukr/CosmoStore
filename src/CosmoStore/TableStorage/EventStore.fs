module CosmoStore.TableStorage.EventStore

open System
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open CosmoStore
open Newtonsoft.Json.Linq
open FSharp.Control.Tasks.V2

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
                match! streamId |> tryGetStreamMetadata table with
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

let getEventStore (configuration:Configuration) = 
    let credentials = Auth.StorageCredentials(configuration.AccountName, configuration.AuthKey)
    let account = CloudStorageAccount(credentials, true)
    let client = account.CreateCloudTableClient()
    
    let dummy = {
        Id = Guid.Empty
        CorrelationId = Guid.Empty
        StreamId = ""
        Position = 0L
        Name = ""
        Data = JValue("") :> JToken
        Metadata = None
        CreatedUtc = DateTime.UtcNow
    }

    {
        AppendEvent = appendEvent client
        AppendEvents = appendEvents client
        GetEvent = fun _ _ -> task { return dummy }//: string -> int64 -> Task<EventRead>
        GetEvents = fun _ _ -> task { return [dummy]}// string -> EventsReadRange -> Task<EventRead list>
        GetStreams = fun _ -> task { return []}// StreamsReadFilter -> Task<string list>
    }