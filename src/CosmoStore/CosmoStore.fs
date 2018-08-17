namespace CosmoStore

open System
open System.Threading.Tasks
open Newtonsoft.Json.Linq

type ExpectedPosition =
    | Any
    | NoStream
    | Exact of int64

type EventsReadRange =
    | AllEvents
    | FromPosition of int64
    | ToPosition of int64
    | PositionRange of fromPosition:int64 * toPosition:int64

type StreamsReadFilter =
    | AllStreams
    | StarsWith of string
    | EndsWith of string
    | Contains of string

type EventRead = {
    Id : Guid
    CorrelationId : Guid
    StreamId : string
    Position: int64
    Name : string
    Data : JToken
    Metadata : JToken option
    CreatedUtc : DateTime
}

type EventWrite = {
    Id : Guid
    CorrelationId : Guid
    Name : string
    Data : JToken
    Metadata : JToken option
}

type Stream = {
    Id : string
    LastPosition : int64
    LastUpdatedUtc: DateTime
}

type EventStore = {
    AppendEvent : string -> ExpectedPosition -> EventWrite -> Task<EventRead>
    AppendEvents : string -> ExpectedPosition -> EventWrite list -> Task<EventRead list>
    GetEvent : string -> int64 -> Task<EventRead>
    GetEvents : string -> EventsReadRange -> Task<EventRead list>
    GetStreams : StreamsReadFilter -> Task<Stream list>
}