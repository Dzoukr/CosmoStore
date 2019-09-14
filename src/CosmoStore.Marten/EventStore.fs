namespace CosmoStore.Marten
open Npgsql
open Newtonsoft.Json
open Newtonsoft.Json.FSharp.Idiomatic

module EventStore =
    open System
    open Marten
    open System.Linq
    open CosmoStore
    open FSharp.Control.Tasks.V2.ContextInsensitive
    open System.Reactive.Linq
    open System.Reactive.Concurrency

    type StreamData = {
        Store: IDocumentStore
        StreamId: string
        ExpectedPosition: ExpectedPosition
        EventWrites: EventWrite list
    }
    type AgentResponse = | EventReads of EventRead list | Error of exn

    type StreamMessage = Data of StreamData * AsyncReplyChannel<AgentResponse>

    let agent =
            let processEvents message = task {
                use session = message.Store.LightweightSession()
                let! lastPosition, metadataEntity = 
                    task {
                        let! res = session |> Session.loadByStringTask<Stream> message.StreamId
                        match res with
                        | Some r ->
                            return r.LastPosition, Some r
                        | None -> 
                            return 0L, None
                    }

                let nextPos = lastPosition + 1L

                do Validation.validatePosition message.StreamId nextPos message.ExpectedPosition

                let ops =
                    message.EventWrites
                    |> List.mapi (fun i evn -> evn |> Conversion.eventWriteToEventRead message.StreamId (nextPos + (int64 i)) DateTime.UtcNow)

                let _ =
                    match metadataEntity with
                    | Some s ->
                        session.Store<Stream>({ s with LastPosition = (s.LastPosition + (int64 message.EventWrites.Length)); LastUpdatedUtc = DateTime.UtcNow })
                    | None ->
                        session.Store<Stream>({ Id = message.StreamId; LastPosition = (int64 message.EventWrites.Length); LastUpdatedUtc = DateTime.UtcNow })

                let _ = session.Store<EventRead>(ops |> List.toArray)
                do! session |> Session.saveChangesTask
                return ops
            }

            MailboxProcessor<StreamMessage>.Start(fun inbox ->

            let rec loop() =
                async {
                    let! message = inbox.Receive()
                    match message with
                    | Data(m, r) ->
                        try
                            let! ops = processEvents m |> Async.AwaitTask
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
            let getEventReads() = task {
                let! res = agent.PostAndAsyncReply(fun replyChannel -> Data(message, replyChannel))
                return
                    match res with
                    | EventReads ers -> ers
                    | Error ex -> raise ex
            }
            return! getEventReads()
        }
    let private getEvents (store: IDocumentStore) (streamId: string) (eventsRead: EventsReadRange) = task {
        use session = store.LightweightSession()
        let! fetch =
            match eventsRead with
            | AllEvents -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId @> 
            | FromPosition f -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId && x.Position >= f @>
            | ToPosition t -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId && x.Position > 0L && x.Position <= t @>
            | PositionRange(f, t) -> session |> Session.query<EventRead> |> Queryable.filter <@ fun x -> x.StreamId = streamId && x.Position >= f && x.Position <= t @>
            |> Queryable.orderBy <@ fun x -> x.Position @>
            |> Queryable.toListTask
        let res = fetch |> Seq.toList
        return res
    }
    let private getEvent store streamId position = task {
        let filter = EventsReadRange.PositionRange(position, position + 1L)
        let! events = getEvents store streamId filter
        return events.Head
    }
    open Microsoft.FSharp.Quotations.Patterns
    
    let rec private propertyName quotation =
        match quotation with
        | PropertyGet (_,propertyInfo,_) -> propertyInfo.Name
        | Lambda (_,expr) -> propertyName expr
        | _ -> ""

    // Get a type safe name in case this changes somehow
    let private correlationIdPropName = propertyName <@ fun (x : EventRead) -> x.CorrelationId @>

    let private getEventsByCorrelationIdQuery = sprintf "where data->>'%s' = ?" correlationIdPropName

    let private getEventsByCorrelationId (store: IDocumentStore) (corrId : Guid) =
        task {
            use session = store.LightweightSession()
            
            let! res = 
                session
                |> Session.sqlTask<EventRead> getEventsByCorrelationIdQuery [| box (corrId.ToString()) |]

            return res |> Seq.toList
        }

    let private getStreams (store: IDocumentStore) streamsRead = task {
        use session = store.LightweightSession()
        let! res =
            match streamsRead with
            | AllStreams -> session |> Session.query<Stream> :> IQueryable<_>
            | Contains c -> session |> Session.query<Stream> |> Queryable.filter <@ fun x -> x.Id.Contains(c) @> 
            | EndsWith c -> session |> Session.query<Stream> |> Queryable.filter <@ fun x -> x.Id.EndsWith(c) @> 
            | StartsWith c -> session |> Session.query<Stream> |> Queryable.filter <@ fun x -> x.Id.StartsWith(c) @> 
            |> Queryable.orderBy <@ fun x -> x.Id @>  
            |> Queryable.toListTask
            
        return (res |> Seq.toList)
    }

    let private getStream (store: IDocumentStore) streamId = task {
        use session = store.LightweightSession()
        let! res = session |> Session.loadByStringTask<Stream> streamId
        match res with
        | Some r -> return r
        | None -> return failwithf "SessionId %s is not present in database" streamId
    }

    let createConnString host user pass database =
        sprintf "Host=%s;Username=%s;Password=%s;Database=%s" host user pass database
        |> NpgsqlConnectionStringBuilder
    
    let userConnStr(conf) = createConnString (conf.Host) (conf.Username) (conf.Password) (conf.Database)
    
    let converters: JsonConverter [] = [|
        OptionConverter()
        SingleCaseDuConverter()
        MultiCaseDuConverter()
    |]

    let martenSerializer =
        // Need to tell marten to use enums as strings or we have to teach npgsql about our enums when using parameters
        let s = Services.JsonNetSerializer(EnumStorage = EnumStorage.AsString)

        for c in converters do
            s.Customize(fun s -> s.Converters.Add(c))
        s

    let getEventStore (conf:  Configuration) =
        let store =
            Marten.DocumentStore.For(
                fun ds ->
                    ds.Connection(userConnStr conf |> string) |> ignore
                    ds.Serializer(martenSerializer) |> ignore
            )

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
