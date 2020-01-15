namespace CosmoStore.ServiceStack

open System
open ServiceStack.Text
open CosmoStore
open ServiceStack.Data
open ServiceStack.OrmLite
open FSharp.Control.Tasks.V2
open System.Reactive.Linq
open System.Reactive.Concurrency
    
module EventStore =

    type StreamData<'payload, 'version> =
        { StreamStore: IDbConnectionFactory
          StreamId: string
          ExpectedVersion: ExpectedVersion<'version>
          EventWrites: EventWrite<'payload> list }

    let checkNull a = obj.ReferenceEquals(a, null)

    let processEvents (message: StreamData<_, _>) (serializer: IStringSerializer) =
        task {
            use! conn = message.StreamStore.OpenAsync()
            use trans = conn.OpenTransaction()
            conn.CreateTableIfNotExists
                ([| typeof<StreamDB>
                    typeof<EventReadDB> |])
            let! lastVersion, metadataEntity = task {
                                                   let! findStream = conn.SingleByIdAsync<StreamDB>(message.StreamId)
                                                   if (checkNull findStream) then return 0L, None
                                                   else return (findStream.LastVersion, (Some findStream))
                                               }
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

            conn.Save<StreamDB>(updatedStream) |> ignore
            conn.SaveAll<EventReadDB>(ops |> List.map (EventReadDB.From serializer)) |> ignore
            trans.Commit()
            return ops
        }

    let private appendEvents (db: IDbConnectionFactory) (serializer: IStringSerializer) (streamId: string) (expectedVersion: ExpectedVersion<_>)
        (events: EventWrite<_> list) =
        task {
            let message =
                { StreamStore = db
                  StreamId = streamId
                  ExpectedVersion = expectedVersion
                  EventWrites = events }

            return! processEvents message serializer
        }

    let private getEvents (db: IDbConnectionFactory) (serializer: IStringSerializer) (streamId: string) (eventsRead: EventsReadRange<_>) =
        task {
            use! conn = db.OpenAsync()

            let! fetch = match eventsRead with
                         | AllEvents ->
                             conn.SelectAsync<EventReadDB>(fun (x: EventReadDB) -> x.StreamId = streamId)
                         | FromVersion f ->
                             conn.SelectAsync<EventReadDB>(fun (x: EventReadDB) ->
                                 x.StreamId = streamId && x.Version >= f)
                         | ToVersion t ->
                             conn.SelectAsync<EventReadDB>(fun (x: EventReadDB) ->
                                 x.StreamId = streamId && x.Version > 0L && x.Version <= t)
                         | VersionRange(f, t) ->
                             conn.SelectAsync<EventReadDB>(fun (x: EventReadDB) ->
                                 x.StreamId = streamId && x.Version >= f && x.Version <= t)

            let res =
                fetch
                |> Seq.map (fun x -> x.To serializer)
                |> Seq.sortBy (fun x -> x.Version)
                |> Seq.toList
            return res
        }

    let private getEvent (db) (serializer: IStringSerializer) streamId version =
        task {
            let filter = EventsReadRange.VersionRange(version, version + 1L)
            let! events = getEvents db serializer streamId filter
            return events.Head
        }

    let private getEventsByCorrelationId (db: IDbConnectionFactory) (serializer: IStringSerializer) (corrId: Guid) =
        task {
            use! conn = db.OpenAsync()

            let res =
                conn.Select<EventReadDB>(fun (x: EventReadDB) -> x.CorrelationId = Nullable(corrId))
                |> Seq.map (fun x -> x.To serializer)
                |> Seq.toList
            return res
        }

    let private getStreams (db: IDbConnectionFactory) (streamsRead: StreamsReadFilter) =
        task {
            use! conn = db.OpenAsync()

            let! sQ = match streamsRead with
                      | AllStreams -> conn.SelectAsync<StreamDB>()
                      | Contains c -> conn.SelectAsync<StreamDB>(fun (x: StreamDB) -> x.Id.Contains(c))
                      | EndsWith c -> conn.SelectAsync<StreamDB>(fun (x: StreamDB) -> x.Id.EndsWith(c))
                      | StartsWith c -> conn.SelectAsync<StreamDB>(fun (x: StreamDB) -> x.Id.StartsWith(c))
            return sQ
                   |> Seq.map (fun x -> x.To)
                   |> Seq.sortBy (fun x -> x.Id)
                   |> Seq.toList
        }

    let private getStream (db: IDbConnectionFactory) (streamId: string) =
        task {
            use! conn = db.OpenAsync()
            let! res = conn.SingleByIdAsync<StreamDB>(streamId)
            return res.To }

    let getEventStore (configuration: Configuration) =
        let db = configuration.Factory
        let ser = configuration.Serializer

        let eventAppended = Event<EventRead<_, _>>()


        { AppendEvent =
              fun stream pos event ->
                  task {
                      let! events = appendEvents db ser stream pos [ event ]
                      events |> List.iter eventAppended.Trigger
                      return events |> List.head
                  }
          AppendEvents =
              fun stream pos events ->
                  task {
                      if events |> List.isEmpty then
                          return []
                      else
                          let! events = appendEvents db ser stream pos events
                          events |> List.iter eventAppended.Trigger
                          return events
                  }
          GetEvent = getEvent db ser
          GetEvents = getEvents db ser
          GetEventsByCorrelationId = getEventsByCorrelationId db ser
          GetStreams = getStreams db
          GetStream = getStream db
          EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance) }
