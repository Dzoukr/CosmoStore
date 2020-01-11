module CosmoStore.LiteDb.EventStore

open Store
open CosmoStore
open FSharp.Control.Tasks.V2
open System
open System.Reactive.Linq
open System.Reactive.Concurrency
open LiteDB
open LiteDB.FSharp.Extensions

[<Literal>]
let private eventCollection = "events"

[<Literal>]
let private streamCollection = "streams"


let eventsDb (db: LiteDatabase) = db.GetCollection<EventRead<_, _>>(eventCollection)
let streamsDb (db: LiteDatabase) = db.GetCollection<Stream<_>>(streamCollection)

let checkNull a = obj.ReferenceEquals(a, null)


type StreamData<'payload, 'version> =
    { DB: LiteDatabase
      StreamId: string
      ExpectedVersion: ExpectedVersion<'version>
      EventWrites: EventWrite<'payload> list }

let processEvents message =
    task {
        let streams = streamsDb message.DB

        let lastVersion, metadataEntity =
            let findStream = streams.FindById(BsonValue(message.StreamId))
            if (checkNull findStream) then 0L, None
            else findStream.LastVersion, (Some findStream)


        let nextPos = lastVersion + 1L
        do Validation.validateVersion message.StreamId nextPos message.ExpectedVersion

        let ops =
            message.EventWrites
            |> List.mapi (fun i evn ->
                evn |> Conversion.eventWriteToEventRead message.StreamId (nextPos + (int64 i)) DateTime.UtcNow)


        let updatedStream =
            match metadataEntity with
            | Some s ->
                { s with
                      LastVersion = (s.LastVersion + (int64 message.EventWrites.Length))
                      LastUpdatedUtc = DateTime.UtcNow }
            | None ->
                { Id = message.StreamId
                  LastVersion = (int64 message.EventWrites.Length)
                  LastUpdatedUtc = DateTime.UtcNow }

        streams.Upsert(BsonValue updatedStream.Id, updatedStream) |> ignore
        let events = eventsDb message.DB
        let _ = events.InsertBulk ops
        return ops
    }





let private appendEvents (db: LiteDatabase) (streamId: string) (expectedVersion: ExpectedVersion<_>)
    (events: EventWrite<_> list) =
    task {
        let message =
            { DB = db
              StreamId = streamId
              ExpectedVersion = expectedVersion
              EventWrites = events }

        return! processEvents message
    }



let private getEvents (db: LiteDatabase) (streamId: string) (eventsRead: EventsReadRange<_>) =
    task {
        let events = eventsDb db

        let fetch =
            match eventsRead with
            | AllEvents -> events.findMany <@ fun x -> x.StreamId = streamId @>
            | FromVersion f ->
                events.findMany
                    <@ fun x -> x.StreamId = streamId && x.Version >= f @> //Query.GTE("Position", BsonValue(f))
            | ToVersion t ->
                events.findMany
                    <@ fun x -> x.StreamId = streamId && x.Version > 0L && x.Version <= t @> //Query.Between("Position",BsonValue(0L), BsonValue(t))
            | VersionRange(f, t) ->
                events.findMany
                    <@ fun x -> x.StreamId = streamId && x.Version >= f && x.Version <= t @> //Query.Between("Position",BsonValue(f), BsonValue(t))


        let res =
            fetch
            |> Seq.sortBy (fun x -> x.Version)
            |> Seq.toList

        return res
    }


let private getEvent (db) streamId version =
    task {
        let filter = EventsReadRange.VersionRange(version, version + 1L)
        let! events = getEvents db streamId filter
        return events.Head
    }


let private getEventsByCorrelationId (db: LiteDatabase) (corrId: Guid) =
    task {
        let events = eventsDb db

        let res =
            events.findMany <@ fun x -> x.CorrelationId = Some corrId @> |> Seq.toList
        return res
    }


let private getStreams (db: LiteDatabase) (streamsRead: StreamsReadFilter) =
    task {
        let streams = streamsDb db

        let sQ =
            match streamsRead with
            | AllStreams -> streams.FindAll()
            | Contains c -> streams.Find(fun x -> x.Id.Contains(c))
            | EndsWith c -> streams.Find(fun x -> x.Id.EndsWith(c))
            | StartsWith c -> streams.Find(fun x -> x.Id.StartsWith(c))
        return sQ
               |> Seq.sortBy (fun x -> x.Id)
               |> Seq.toList
    }


let private getStream (db: LiteDatabase) (streamId: string) =
    task {
        let streams = streamsDb db
        return (streams.FindById(BsonValue(streamId)))
    }





let getEventStore (configuration: Configuration) =
    let db = createDatabaseUsing configuration

    let eventAppended = Event<EventRead<_, _>>()


    { AppendEvent =
          fun stream pos event ->
              task {
                  let! events = appendEvents db stream pos [ event ]
                  events |> List.iter eventAppended.Trigger
                  return events |> List.head
              }
      AppendEvents =
          fun stream pos events ->
              task {
                  if events |> List.isEmpty then
                      return []
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
      EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance) }
