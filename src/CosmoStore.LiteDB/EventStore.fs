module CosmoStore.LiteDb.EventStore
open Store
open CosmoStore
open FSharp.Control.Tasks.V2
open Newtonsoft.Json.Linq
open System
open System.Reactive.Linq
open System.Reactive.Concurrency
open LiteDB
open LiteDB.FSharp

[<Literal>]
let private eventCollection = "events";
[<Literal>]
let private streamCollection = "streams";


let private validatePosition streamId (nextPos:int64) = function
    | ExpectedPosition.Any -> ()
    | ExpectedPosition.NoStream -> 
        if nextPos > 1L then 
            failwithf "ESERROR_POSITION_STREAMEXISTS: Stream '%s' was expected to be empty, but contains %i events" streamId (nextPos - 1L)
    | ExpectedPosition.Exact expectedPos ->
        if nextPos <> expectedPos then
            failwithf "ESERROR_POSITION_POSITIONNOTMATCH: Stream '%s' was expected to have next position %i, but has %i" streamId expectedPos nextPos

let eventsDb (db: LiteDatabase) =  db.GetCollection<EventRead>(eventCollection)
let streamsDb (db : LiteDatabase) = db.GetCollection<Stream>(streamCollection)

let checkNull a = obj.ReferenceEquals(a, null)

let eventWriteToEventRead streamId position createdUtc (x:EventWrite) = {
    Id = x.Id
    CorrelationId = x.CorrelationId
    CausationId = x.CausationId
    StreamId = streamId
    Position = position
    Name = x.Name
    Data = x.Data
    Metadata = x.Metadata
    CreatedUtc = createdUtc
}


let private appendEvents (db) (streamId: string) (expectedPosition : ExpectedPosition ) (events : EventWrite list) = 
    task {
        let streams = streamsDb db
        let lastPosition, metadataEntity = 
            let findStream = streams.FindById(BsonValue(streamId))
            if (checkNull findStream) then 0L, None else findStream.LastPosition,(Some findStream)
            

        let nextPos = lastPosition + 1L        
        do validatePosition streamId nextPos expectedPosition

        let ops = 
            events
            |> List.mapi (fun i evn -> 
                evn |> eventWriteToEventRead streamId (nextPos + (int64 i)) DateTime.UtcNow
            )

        let res =
            match metadataEntity with 
            | Some s -> 
                let b = streams.Update ({s with LastPosition = (s.LastPosition + (int64 events.Length)); LastUpdatedUtc = DateTime.UtcNow })
                if b then () else failwithf "Stream update failed with stream id %s" s.Id
            | None -> 
                streams.Insert({Id = streamId; LastPosition = (int64 events.Length); LastUpdatedUtc = DateTime.UtcNow}) |> ignore //TODO: make use of ID if required 
            let events = eventsDb db
            let _ = events.InsertBulk ops

            ops

        return res
    }


let private getEvents (db: LiteDatabase) (streamId : string) (eventsRead:EventsReadRange) = 
    task {
        let rangeQ = 
            match eventsRead with
            | AllEvents -> Query.GTE("LastPosition", BsonValue(0L))
            | FromPosition f -> Query.GTE("LastPosition", BsonValue(f))
            | ToPosition t -> Query.Between("LastPosition",BsonValue(0L), BsonValue(t))
            | PositionRange (f, t) -> Query.Between("LastPosition",BsonValue(f), BsonValue(t))
        let q = Query.And(Query.EQ("StreamId", BsonValue(streamId)), rangeQ)
        let events = eventsDb db
        printfn "Some list if there is any: %A" (events.FindAll())
        let res = events.Find(q) |> Seq.sortBy(fun x -> x.Position) |>  Seq.toList
        return res
    }
 
let private getEvent (db) streamId position =
        task {
            let filter = EventsReadRange.PositionRange(position, position)
            let! events = getEvents db streamId filter
            return events.Head
        }

let private getEventsByCorrelationId (db:LiteDatabase) (corrId : Guid)= 
    task {
        let q = Query.And(Query.EQ("CorrelationId", BsonValue(corrId)))
        let events = eventsDb db
        let res = events.Find(q) |> Seq.sortBy(fun x -> x.CreatedUtc) |> Seq.toList
        return res
    }
let private getStreams (db :LiteDatabase) (streamsRead:StreamsReadFilter) = 
    task {
        let streams = streamsDb db
        let sQ = 
            match streamsRead with 
            | AllStreams -> streams.FindAll()
            | Contains c -> streams.Find(Query.Contains("Id", c))
            | EndsWith c -> streams.Find(Query.Contains("Id", c)) //TODO: endwith does not exist in litedb
            | StartsWith c -> streams.Find(Query.StartsWith("Id", c))
        return sQ |> Seq.toList
    }
let private getStream (db :LiteDatabase) (streamId : string) = 
    task {
        let streams = streamsDb db
        return (streams.FindById(BsonValue (streamId)))
    }




let getEventStore (configuration:Configuration) = 
    let db = createDatabaseUsing configuration

    let eventAppended = Event<EventRead>()


    {
        AppendEvent = fun stream pos event -> task {
            let! events = appendEvents db stream pos [event]
            events |> List.iter eventAppended.Trigger
            return events |> List.head
        }
        AppendEvents = fun stream pos events -> task {
            if events |> List.isEmpty then return []
            else 
                let! events = appendEvents db stream pos events
                events |> List.iter eventAppended.Trigger
                return events
        }
        GetEvent = getEvent db
        GetEvents = getEvents db
        GetEventsByCorrelationId = getEventsByCorrelationId db
        GetStreams = getStreams db
        GetStream = getStream db
        EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
    }
