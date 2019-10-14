namespace CosmoStore

open System
open System.Threading.Tasks

type StreamId = string

type ExpectedVersion<'version> =
    | Any
    | NoStream
    | Exact of 'version

type EventsReadRange<'version> =
    | AllEvents
    | FromVersion of 'version
    | ToVersion of 'version
    | VersionRange of fromVersion:'version * toVersion:'version

type StreamsReadFilter =
    | AllStreams
    | StartsWith of string
    | EndsWith of string
    | Contains of string

type EventRead<'payload,'version> = {
    Id : Guid
    CorrelationId : Guid option
    CausationId : Guid option
    StreamId : StreamId
    Version: 'version
    Name : string
    Data : 'payload
    Metadata : 'payload option
    CreatedUtc : DateTime
}

type EventWrite<'payload> = {
    Id : Guid
    CorrelationId : Guid option
    CausationId : Guid option
    Name : string
    Data : 'payload
    Metadata : 'payload option
}

type Stream<'version> = {
    Id : StreamId
    LastVersion : 'version
    LastUpdatedUtc: DateTime
}

type EventStore<'payload,'version> = {
    AppendEvent : StreamId -> ExpectedVersion<'version> -> EventWrite<'payload> -> Task<EventRead<'payload,'version>>
    AppendEvents : StreamId -> ExpectedVersion<'version> -> EventWrite<'payload> list -> Task<EventRead<'payload,'version> list>
    GetEvent : StreamId -> 'version -> Task<EventRead<'payload,'version>>
    GetEvents : StreamId -> EventsReadRange<'version> -> Task<EventRead<'payload,'version> list>
    GetEventsByCorrelationId : Guid -> Task<EventRead<'payload,'version> list>
    GetStreams : StreamsReadFilter -> Task<Stream<'version> list>
    GetStream : StreamId -> Task<Stream<'version>>
    EventAppended : IObservable<EventRead<'payload,'version>>
}