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
    | StartsWith of string
    | EndsWith of string
    | Contains of string

type EventRead = {
    Id : Guid
    CorrelationId : Guid option
    CausationId : Guid option
    StreamId : string
    Position: int64
    Name : string
    Data : JToken
    Metadata : JToken option
    CreatedUtc : DateTime
}

type EventWrite = {
    Id : Guid
    CorrelationId : Guid option
    CausationId : Guid option
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
    GetEventsByCorrelationId : Guid -> Task<EventRead list>
    GetStreams : StreamsReadFilter -> Task<Stream list>
    GetStream : string -> Task<Stream>
    EventAppended : IObservable<EventRead>
}

module Helper =
    let validatePosition streamId (nextPos: int64) = function
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