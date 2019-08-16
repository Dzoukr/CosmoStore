namespace CosmoStore

open System
open System.Threading.Tasks

type StreamId = string

type ExpectedPosition<'position> =
    | Any
    | NoStream
    | Exact of 'position

type EventsReadRange<'position> =
    | AllEvents
    | FromPosition of 'position
    | ToPosition of 'position
    | PositionRange of fromPosition:'position * toPosition:'position

type StreamsReadFilter =
    | AllStreams
    | StartsWith of string
    | EndsWith of string
    | Contains of string

type EventRead<'payload,'position> = {
    Id : Guid
    CorrelationId : Guid option
    CausationId : Guid option
    StreamId : StreamId
    Position: 'position
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

type Stream<'position> = {
    Id : StreamId
    LastPosition : 'position
    LastUpdatedUtc: DateTime
}

type EventStore<'payload,'position> = {
    AppendEvent : StreamId -> ExpectedPosition<'position> -> EventWrite<'payload> -> Task<EventRead<'payload,'position>>
    AppendEvents : StreamId -> ExpectedPosition<'position> -> EventWrite<'payload> list -> Task<EventRead<'payload,'position> list>
    GetEvent : StreamId -> 'position -> Task<EventRead<'payload,'position>>
    GetEvents : StreamId -> EventsReadRange<'position> -> Task<EventRead<'payload,'position> list>
    GetEventsByCorrelationId : Guid -> Task<EventRead<'payload,'position> list>
    GetStreams : StreamsReadFilter -> Task<Stream<'position> list>
    GetStream : StreamId -> Task<Stream<'position>>
    EventAppended : IObservable<EventRead<'payload,'position>>
}