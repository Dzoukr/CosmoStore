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
open System.Threading.Tasks

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

type StreamData = {
    DB : LiteDatabase
    StreamId : string
    ExpectedPosition : ExpectedPosition
    EventWrites : EventWrite list
}

type AgentResponse = EventReads of EventRead list | Error of exn

type StreamMessage = Data of  StreamData * AsyncReplyChannel<AgentResponse>


let agent<'a,'b>  = MailboxProcessor<StreamMessage>.Start(fun inbox ->
    let processEvents message = 
        let streams = streamsDb message.DB
        let lastPosition, metadataEntity =
            let findStream = streams.FindById(BsonValue(message.StreamId))
            if (checkNull findStream) then 0L, None else findStream.LastPosition,(Some findStream)


        let nextPos = lastPosition + 1L
        do validatePosition message.StreamId nextPos message.ExpectedPosition

        let ops =
            message.EventWrites
            |> List.mapi (fun i evn ->
                evn |> eventWriteToEventRead message.StreamId (nextPos + (int64 i)) DateTime.UtcNow
            )

        let res =
            match metadataEntity with
            | Some s ->
                let b = streams.Update ({s with LastPosition = (s.LastPosition + (int64 message.EventWrites.Length)); LastUpdatedUtc = DateTime.UtcNow })
                if b then () else failwithf "Stream update failed with stream id %s" s.Id
            | None ->
                streams.Insert({Id = message.StreamId; LastPosition = (int64  message.EventWrites.Length); LastUpdatedUtc = DateTime.UtcNow}) |> ignore //TODO: make use of ID if required
        let events = eventsDb message.DB
        let _ = events.InsertBulk ops
        ops
    
    
    let rec loop() =
        async {
                let! message = inbox.Receive();

                match message with 
                | Data (m, r) ->
                    try
                        let ops = processEvents m
                        r.Reply(EventReads ops)
                    with exn ->
                        r.Reply(Error exn)
                    return! loop()
        }
    loop())



let private appendEvents (db) (streamId: string) (expectedPosition : ExpectedPosition ) (events : EventWrite list) : (Task<EventRead list>) =
    //Litedb is single file database so multi thread access kind of get crazy. That is the reason putting everything in single queue to process

    let message = {
        DB = db
        StreamId = streamId
        ExpectedPosition = expectedPosition
        EventWrites = events
    }

    let getReads() = 
        let res = agent.PostAndAsyncReply (fun replyChannel -> Data (message, replyChannel)) |> Async.RunSynchronously
        match res with 
        | EventReads ers -> ers
        | Error ex -> raise ex

    task {
        return getReads()
    }


let private getEvents (db: LiteDatabase) (streamId : string) (eventsRead:EventsReadRange) =
    task {
        let events = eventsDb db
        let fetch =
            match eventsRead with
            | AllEvents -> events.Find(fun x -> x.StreamId = streamId)
            | FromPosition f -> events.Find(fun x -> x.StreamId = streamId && x.Position >= f) //Query.GTE("Position", BsonValue(f))
            | ToPosition t -> events.Find(fun x -> x.StreamId = streamId && x.Position > 0L && x.Position <= t) //Query.Between("Position",BsonValue(0L), BsonValue(t))
            | PositionRange (f, t) -> events.Find(fun x -> x.StreamId = streamId && x.Position >= f && x.Position <= t) //Query.Between("Position",BsonValue(f), BsonValue(t))


        let res = fetch |> Seq.sortBy(fun x -> x.Position) |>  Seq.toList
        return res
    }

let private getEvent (db) streamId position =
        task {
            let filter = EventsReadRange.PositionRange(position, position + 1L)
            let! events = getEvents db streamId filter
            return events.Head
        }

let private getEventsByCorrelationId (db:LiteDatabase) (corrId : Guid)=
    task {
        let events = eventsDb db
        //TODO: Some is not getting compared for litedb query.
        let res = events.FindAll() |> Seq.filter(fun x -> x.CorrelationId = Some corrId) |> Seq.toList //events.Find(fun x -> x.CorrelationId = Some corrId) |> Seq.sortBy(fun x -> x.CreatedUtc) |> Seq.toList
        return res
    }
let private getStreams (db :LiteDatabase) (streamsRead:StreamsReadFilter) =
    task {
        let streams = streamsDb db
        let sQ =
            match streamsRead with
            | AllStreams -> streams.FindAll()
            | Contains c -> streams.Find(fun x -> x.Id.Contains(c))
            | EndsWith c -> streams.Find(fun x -> x.Id.EndsWith(c))
            | StartsWith c -> streams.Find(fun x -> x.Id.StartsWith(c))
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
