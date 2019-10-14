namespace CosmoStore.InMemory

open System
open CosmoStore
open FSharp.Control.Tasks.V2
open Newtonsoft.Json.Linq
open System.Reactive.Linq
open System.Reactive.Concurrency

module EventStore =

    type StreamData<'payload, 'version> = {
        StreamStore: StreamStoreType<'version>
        EventStore: EventStoreType<'payload, 'version>
        StreamId: string
        ExpectedVersion: ExpectedVersion<'version>
        EventWrites: EventWrite<'payload> list
    }
    type AgentResponse<'payload, 'version> = | EventReads of EventRead<'payload, 'version> list | Error of exn

    type StreamMessage<'payload, 'version> = Data of StreamData<'payload, 'version> * AsyncReplyChannel<AgentResponse<'payload, 'version>>

    let agent : MailboxProcessor<StreamMessage<JToken, int64>> =
            let processEvents (message: StreamData<_,_>) =
                let lastVersion, metadataEntity =
                    let res = message.StreamStore.ContainsKey(message.StreamId) |> fun x -> if x then Some message.StreamStore.[message.StreamId] else None
                    match res with
                    | Some r ->
                        r.LastVersion, Some r
                    | None -> 0L, None

                let nextPos = lastVersion + 1L

                do Validation.validateVersion message.StreamId nextPos message.ExpectedVersion

                let ops =
                    message.EventWrites
                    |> List.mapi (fun i evn -> evn |> Conversion.eventWriteToEventRead message.StreamId (nextPos + (int64 i)) DateTime.UtcNow)

                let updatedStream =
                    match metadataEntity with
                    | Some s ->
                        { s with LastVersion = (s.LastVersion + (int64 message.EventWrites.Length)); LastUpdatedUtc = DateTime.UtcNow }

                    | None ->
                        { Id = message.StreamId; LastVersion = (int64 message.EventWrites.Length); LastUpdatedUtc = DateTime.UtcNow }
                message.StreamStore.AddOrUpdate(updatedStream.Id, updatedStream, fun _ _ -> updatedStream) |> ignore
                ops |> List.map (fun x -> message.EventStore.TryAdd(x.Id, x)) |> List.fold (fun acc x -> x && acc) true |> ignore
                ops

            MailboxProcessor<StreamMessage<_,_>>.Start(fun inbox ->

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

    let private appendEvents (streamStore) (eventStore) (streamId: string) (expectedVersion: ExpectedVersion<_>) (events: EventWrite<_> list) =
        task {
            let message = {
                StreamStore = streamStore
                EventStore = eventStore
                StreamId = streamId
                ExpectedVersion = expectedVersion
                EventWrites = events
            }
            let getEventReads() =
                let res = agent.PostAndReply(fun replyChannel -> Data(message, replyChannel))
                match res with
                | EventReads ers -> ers
                | Error ex -> raise ex
            return getEventReads()
        }
    let private getEvents (store: EventStoreType<_,_>) (streamId: string) (eventsRead: EventsReadRange<_>) = task {
        let fetch =
            let currentStreamEvents = store.Values |> Seq.filter (fun x -> x.StreamId = streamId)
            match eventsRead with
            | AllEvents -> currentStreamEvents
            | FromVersion f -> currentStreamEvents |> Seq.filter (fun x -> x.Version >= f)
            | ToVersion t -> currentStreamEvents |> Seq.filter (fun x -> x.Version > 0L && x.Version <= t)
            | VersionRange(f, t) -> currentStreamEvents |> Seq.filter (fun x -> x.Version >= f && x.Version <= t)
        let res = fetch |> Seq.sortBy (fun x -> x.Version) |> Seq.toList
        return res
    }
    let private getEvent store streamId version = task {
        let filter = EventsReadRange.VersionRange(version, version + 1L)
        let! events = getEvents store streamId filter
        return events.Head
    }

    let private getEventsByCorrelationId (store: EventStoreType<_,_>) corrId =
        task {
            let res = store.Values |> Seq.filter (fun x -> x.CorrelationId = Some corrId)
            return (res |> Seq.toList)
        }

    let private getStreams (store: StreamStoreType<_>) streamsRead = task {
        let res =
            match streamsRead with
            | AllStreams -> store.Values |> Seq.toList
            | Contains c -> store.Keys |> Seq.filter (fun x -> x.Contains(c)) |> Seq.map (fun x -> store.[x]) |> Seq.toList
            | EndsWith c -> store.Keys |> Seq.filter (fun x -> x.EndsWith(c)) |> Seq.map (fun x -> store.[x]) |> Seq.toList
            | StartsWith c -> store.Keys |> Seq.filter (fun x -> x.StartsWith(c)) |> Seq.map (fun x -> store.[x]) |> Seq.toList
        return (res |> Seq.sortBy(fun x -> x.Id) |> Seq.toList)
     }

    let private getStream (store: StreamStoreType<_>) streamId = task {
        let res = store.ContainsKey(streamId)
        if res then return store.[streamId]
        else return failwithf "SessionId %s is not present in database" streamId
    }
    let getEventStore (configuration: Configuration<_,_>) =
        let streamStore = configuration.InMemoryStreams
        let eventStore = configuration.InMemoryEvents

        let eventAppended = Event<EventRead<_,_>>()


        {
            AppendEvent = fun stream pos event -> task {
                let! events = appendEvents streamStore eventStore stream pos [ event ]
                events |> List.iter eventAppended.Trigger
                return events |> List.head
            }
            AppendEvents = fun stream pos events -> task {
                if events |> List.isEmpty then return []
                else
                    let! events = appendEvents streamStore eventStore stream pos events
                    events |> List.iter eventAppended.Trigger
                    return events
            }
            GetEvent = getEvent eventStore
            GetEvents = getEvents eventStore
            GetEventsByCorrelationId = getEventsByCorrelationId eventStore
            GetStreams = getStreams streamStore
            GetStream = getStream streamStore
            EventAppended = Observable.ObserveOn(eventAppended.Publish :> IObservable<_>, ThreadPoolScheduler.Instance)
        }

