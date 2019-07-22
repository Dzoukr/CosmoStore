namespace CosmoStore

open System
open System.Threading.Tasks

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
    StreamId : string
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
    Id : string
    LastPosition : 'position
    LastUpdatedUtc: DateTime
}

type EventStore<'payload,'position> = {
    AppendEvent : string -> ExpectedPosition<'position> -> EventWrite<'payload> -> Task<EventRead<'payload,'position>>
    AppendEvents : string -> ExpectedPosition<'position> -> EventWrite<'payload> list -> Task<EventRead<'payload,'position> list>
    GetEvent : string -> 'position -> Task<EventRead<'payload,'position>>
    GetEvents : string -> EventsReadRange<'position> -> Task<EventRead<'payload,'position> list>
    GetEventsByCorrelationId : Guid -> Task<EventRead<'payload,'position> list>
    GetStreams : StreamsReadFilter -> Task<Stream<'position> list>
    GetStream : string -> Task<Stream<'position>>
    EventAppended : IObservable<EventRead<'payload,'position>>
}