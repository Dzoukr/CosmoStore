namespace CosmoStore.ServiceStack

open System
open CosmoStore
open ServiceStack.Data
open ServiceStack.OrmLite
open FSharp.Control.Tasks.V2.ContextInsensitive
open ServiceStack.DataAnnotations

[<CLIMutable>]
[<Alias("ss_events")>]
type EventReadDB<'payload, 'version when 'payload: (new: unit -> 'payload) and 'payload: struct and 'payload :> ValueType> =
    { Id: Guid
      CorrelationId: Nullable<Guid>
      CausationId: Nullable<Guid>
      StreamId: StreamId
      Version: 'version
      Name: string
      Data: 'payload
      Metadata: Nullable<'payload>
      CreatedUtc: DateTime }

    member x.To: EventRead<'payload, 'version> =
        { Id = x.Id
          CorrelationId = Option.ofNullable x.CorrelationId
          CausationId = Option.ofNullable x.CausationId
          StreamId = x.StreamId
          Version = x.Version
          Name = x.Name
          Data = x.Data
          Metadata = Option.ofNullable x.Metadata
          CreatedUtc = x.CreatedUtc }

    static member From(x: EventRead<'payload, 'version>): EventReadDB<'payload, 'version> =
        { Id = x.Id
          CorrelationId = Option.toNullable x.CorrelationId
          CausationId = Option.toNullable x.CausationId
          StreamId = x.StreamId
          Version = x.Version
          Name = x.Name
          Data = x.Data
          Metadata = Option.toNullable x.Metadata
          CreatedUtc = x.CreatedUtc }

[<CLIMutable>]
[<Alias("ss_streams")>]
type StreamDB =
    { Id: StreamId
      LastVersion: int64
      LastUpdatedUtc: DateTime }

    member x.To: Stream<int64> =
        { Id = x.Id
          LastVersion = x.LastVersion
          LastUpdatedUtc = x.LastUpdatedUtc }

    static member From(x: Stream<int64>): StreamDB =
        { Id = x.Id
          LastVersion = x.LastVersion
          LastUpdatedUtc = x.LastUpdatedUtc }

module EventStore =

    type StreamData<'payload, 'version> =
        { StreamStore: DbConnectionFactory
          StreamId: string
          ExpectedVersion: ExpectedVersion<'version>
          EventWrites: EventWrite<'payload> list }

    let checkNull a = obj.ReferenceEquals(a, null)

    let processEvents (message: StreamData<_, _>) =
        task {
            use! conn = message.StreamStore.OpenAsync()
            conn.CreateTableIfNotExists
                ([| typeof<StreamDB>
                    typeof<EventReadDB<_, _>> |])
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
            conn.SaveAll<EventReadDB<_, _>>(ops |> List.map EventReadDB.From) |> ignore
            return ops
        }

    let private appendEvents (db: DbConnectionFactory) (streamId: string) (expectedVersion: ExpectedVersion<_>)
        (events: EventWrite<_> list) =
        task {
            let message =
                { StreamStore = db
                  StreamId = streamId
                  ExpectedVersion = expectedVersion
                  EventWrites = events }

            return! processEvents message
        }

    let private getEvents (db: DbConnectionFactory) (streamId: string) (eventsRead: EventsReadRange<_>) =
        task {
            use! conn = db.OpenAsync()

            let q =
                match eventsRead with
                | AllEvents -> conn.From<EventReadDB<_, _>>().Where(fun (x: EventReadDB<_, _>) -> x.StreamId = streamId)
                | FromVersion f ->
                    conn.From<EventReadDB<_, _>>()
                        .Where(fun (x: EventReadDB<_, _>) -> x.StreamId = streamId && x.Version >= f)
                | ToVersion t ->
                    conn.From<EventReadDB<_, _>>()
                        .Where(fun (x: EventReadDB<_, _>) -> x.StreamId = streamId && x.Version > 0L && x.Version <= t)
                | VersionRange(f, t) ->
                    conn.From<EventReadDB<_, _>>()
                        .Where(fun (x: EventReadDB<_, _>) -> x.StreamId = streamId && x.Version >= f && x.Version <= t)
            let! fetch = conn.SelectAsync<EventReadDB<_, _>>(q)

            let res =
                fetch
                |> Seq.map(fun x -> x.To)
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
        
    let private getEventsByCorrelationId (db: DbConnectionFactory) (corrId: Guid) =
    task {
        use! conn = db.OpenAsync()

        let res =
            events.findMany <@ fun x -> x.CorrelationId = Some corrId @>
            |> Seq.filter (fun x -> x.CorrelationId = Some corrId)
            |> Seq.toList //events.Find(fun x -> x.CorrelationId = Some corrId) |> Seq.sortBy(fun x -> x.CreatedUtc) |> Seq.toList
        return res
    }