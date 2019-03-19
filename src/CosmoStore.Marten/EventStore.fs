namespace CosmoStore.Marten
open Npgsql


module EventStore =
    open System
    open Marten
    open System.Linq
    open CosmoStore
    open FSharp.Control.Tasks.V2
    open System.Reactive.Linq
    open System.Reactive.Concurrency


    let private validatePosition streamId (nextPos: int64) = function
        | ExpectedPosition.Any -> ()
        | ExpectedPosition.NoStream ->
            if nextPos > 1L then
                failwithf "ESERROR_POSITION_STREAMEXISTS: Stream '%s' was expected to be empty, but contains %i events" streamId (nextPos - 1L)
        | ExpectedPosition.Exact expectedPos ->
            if nextPos <> expectedPos then
                failwithf "ESERROR_POSITION_POSITIONNOTMATCH: Stream '%s' was expected to have next position %i, but has %i" streamId expectedPos nextPos


    let checkNull a = obj.ReferenceEquals(a, null)

    let eventWriteToEventRead streamId position createdUtc (x: EventWrite) = {
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
        Store: IDocumentStore
        StreamId: string
        ExpectedPosition: ExpectedPosition
        EventWrites: EventWrite list
    }
    type AgentResponse = | EventReads of EventRead list | Error of exn

    type StreamMessage = Data of StreamData * AsyncReplyChannel<AgentResponse>

    let agent =
            let processEvents message =
                use session = message.Store.LightweightSession()
                let lastPosition, metadataEntity =
                    let res = session |> Session.loadByString<Stream> message.StreamId
                    match res with
                    | Some r ->
                        r.LastPosition, Some r
                    | None -> 0L, None

                let nextPos = lastPosition + 1L

                do validatePosition message.StreamId nextPos message.ExpectedPosition

                let ops =
                    message.EventWrites
                    |> List.mapi (fun i evn -> evn |> eventWriteToEventRead message.StreamId (nextPos + (int64 i)) DateTime.UtcNow)

                let _ =
                    match metadataEntity with
                    | Some s ->
                        session.Store<Stream>({ s with LastPosition = (s.LastPosition + (int64 message.EventWrites.Length)); LastUpdatedUtc = DateTime.UtcNow })
                    | None ->
                        session.Store<Stream>({ Id = message.StreamId; LastPosition = (int64 message.EventWrites.Length); LastUpdatedUtc = DateTime.UtcNow })

                let _ = session.Store<EventRead>(ops |> List.toArray)
                session.SaveChanges()
                ops

            MailboxProcessor<StreamMessage>.Start(fun inbox ->

            let rec loop() =
                async {
                    let! message = inbox.Receive()
                    match message with
                    | Data(m, r) ->
                        try
                            let ops = processEvents m
                            r.Reply(EventReads ops)
                        with exn ->
                            r.Reply(Error exn)
                        return! loop()
                }
            loop()
        )

    let private appendEvents (store) (streamId: string) (expectedPosition: ExpectedPosition) (events: EventWrite list) =
        task {
            let message = {
                Store = store
                StreamId = streamId
                ExpectedPosition = expectedPosition
                EventWrites = events
            }
            let getEventReads() =
                let res = agent.PostAndReply(fun replyChannel -> Data(message, replyChannel))
                match res with
                | EventReads ers -> ers
                | Error ex -> raise ex
            return getEventReads()
        }
    let private getEvents (store: IDocumentStore) (streamId: string) (eventsRead: EventsReadRange) = task {
        use session = store.LightweightSession()
        let fetch =
            match eventsRead with
            | AllEvents -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId @>
            | FromPosition f -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId && x.Position >= f @>
            | ToPosition t -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId && x.Position > 0L && x.Position <= t @>
            | PositionRange(f, t) -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId && x.Position >= f && x.Position <= t @>
        let res = fetch |> Seq.sortBy (fun x -> x.Position) |> Seq.toList
        return res
    }
    let private getEvent store streamId position = task {
        let filter = EventsReadRange.PositionRange(position, position + 1L)
        let! events = getEvents store streamId filter
        return events.Head
    }

    let private getEventsByCorrelationId (store: IDocumentStore) corrId =
        task {
            use session = store.LightweightSession()

//            let res = session |> Session.query<EventRead> |> Queryable.filter <@ fun x ->  Option.toNullable(x.CorrelationId) = Nullable(corrId) @> |> Seq.toList
//TODO: Option is type is not supported by store. So, it is better to convert option type to Nullable and then put a guard on that.
//TODO: remove this not optimized filter once things converted to nullable. Then above code can be used. Until then don't use in production
            let res = session |> Session.query<EventRead> |> Seq.filter (fun x -> x.CorrelationId = Some corrId) |> Seq.toList
            return (res)
        }

    let private getStreams (store: IDocumentStore) streamsRead = task {
        use session = store.LightweightSession()
        let res =
            match streamsRead with
            | AllStreams -> session |> Session.query<Stream> |> Seq.toList
            | Contains c -> session |> Session.query<Stream> |> Queryable.filter <@ fun x -> x.Id.Contains(c) @> |> Seq.toList
            | EndsWith c -> session |> Session.query<Stream> |> Queryable.filter <@ fun x -> x.Id.EndsWith(c) @> |> Seq.toList
            | StartsWith c -> session |> Session.query<Stream> |> Queryable.filter <@ fun x -> x.Id.StartsWith(c) @> |> Seq.toList
        return (res |> List.sortBy(fun x -> x.Id))
     }

    let private getStream (store: IDocumentStore) streamId = task {
        use session = store.LightweightSession()
        let res = session |> Session.loadByString<Stream> streamId
        match res with
        | Some r -> return r
        | None -> return failwithf "SessionId %s is not present in database" streamId
    }

    let createConnString host user pass database =
        sprintf "Host=%s;Username=%s;Password=%s;Database=%s" host user pass database
        |> NpgsqlConnectionStringBuilder
    
    let userConnStr(conf) = createConnString (conf.Host) (conf.Username) (conf.Password) (conf.Database)
    
    let getEventStore (conf:  Configuration) =
        
        
        let store =
            userConnStr conf
            |> string
            |> DocumentStore.For

        let eventAppended = Event<EventRead>()


        {
            AppendEvent = fun stream pos event -> task {
                let! events = appendEvents store stream pos [ event ]
                events |> List.iter eventAppended.Trigger
                return events |> List.head
            }
            AppendEvents = fun stream pos events -> task {
                if events |> List.isEmpty then return []
                else
                    let! events = appendEvents store stream pos events
                    events |> List.iter eventAppended.Trigger
                    return events
            }
            GetEvent = getEvent store
            GetEvents = getEvents store
            GetEventsByCorrelationId = getEventsByCorrelationId store
            GetStreams = getStreams store
            GetStream = getStream store
            EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
        }
