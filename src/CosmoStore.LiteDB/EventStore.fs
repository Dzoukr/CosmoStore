module CosmoStore.LiteDb.EventStore
open Store
open CosmoStore
open FSharp.Control.Tasks.V2
open Newtonsoft.Json.Linq
open System
open System.Reactive.Linq
open System.Reactive.Concurrency
open LiteDB
open LiteDB.FSharp

[<Literal>]
let private eventCollection = "events";
[<Literal>]
let private streamCollection = "streams";


let private validatePosition streamId (nextPos:int64) = function
    | ExpectedPosition.Any -> ()
    | ExpectedPosition.NoStream -> 
        if nextPos > 1L then 
            failwithf "ESERROR_POSITION_STREAMEXISTS: Stream '%s' was expected to be empty, but contains %i events" streamId (nextPos - 1L)
    | ExpectedPosition.Exact expectedPos ->
        if nextPos <> expectedPos then
            failwithf "ESERROR_POSITION_POSITIONNOTMATCH: Stream '%s' was expected to have next position %i, but has %i" streamId expectedPos nextPos

let eventsDb (db: LiteDatabase) =  db.GetCollection<EventRead>(eventCollection)
let streamsDb (db : LiteDatabase) = db.GetCollection<Stream>(streamCollection)

let checkNull a = obj.ReferenceEquals(a, null)

let eventWriteToEventRead streamId position createdUtc (x:EventWrite) = {
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

let private appendEvent (db) (streamId: string) (pos : ExpectedPosition ) (events : EventWrite list) = 
    task {
        let streams = streamsDb db
        let! currentStream = 
            task {
                let findStream = streams.FindById(BsonValue(streamId))
                let r = if (checkNull findStream) then None else (Some findStream)
                match r with 
                | Some a -> return a
                | None -> 
                    let i = 
                        streams.Insert {
                            Id = streamId
                            LastPosition  = 0L
                            LastUpdatedUtc = DateTime.UtcNow
                        }
                    return (streams.FindById(i)) 
            }
        let nextPos = currentStream.LastPosition + 1L
        do validatePosition currentStream.Id nextPos pos

        //Update stream 
        let! updatedStream = task {
            let item = {
                         Id = currentStream.Id
                         LastPosition = nextPos
                         LastUpdatedUtc = DateTime.UtcNow
                    }
            let b = streams.Update item
            if b then return item else return failwithf "Stream with stream id %s can't be updated" currentStream.Id
        }

        //Insert all events
        events |> List.map (eventWriteToEventRead updatedStream.Id updatedStream.LastPosition  ) |> eve

        return ""
    }

let private appendEvents = ""
let private getEvent = ""
let private getEvents = ""
let private getEventsByCorrelationId = ""
let private getStreams = ""
let private getStream = ""
let private eventAppended = ""




let getEventStore (configuration:Configuration) = 
    let db = createDatabaseUsing configuration
    {
        AppendEvent = fun stream pos event -> task {
            return {
            Id = Guid.Empty
            CorrelationId = None
            CausationId = None
            StreamId = ""
            Position = 0L
            Name  = ""
            Data = JToken.FromObject("")
            Metadata = None
            CreatedUtc  = DateTime.Now }
        }
        AppendEvents = fun stream pos events -> task {return []}
        GetEvent = fun stream pos -> task {
            return {
            Id = Guid.Empty
            CorrelationId = None
            CausationId = None
            StreamId = ""
            Position = 0L
            Name  = ""
            Data = JToken.FromObject(null)
            Metadata = None
            CreatedUtc  = DateTime.Now }
        }
        GetEvents = fun stream range -> task {return []} 
        GetEventsByCorrelationId = fun stream -> task {return []}
        GetStreams = fun filter -> task {return []}
        GetStream = fun stream -> task {
             return {
                 Id  = ""
                 LastPosition = 0L
                 LastUpdatedUtc = DateTime.Now
             }
        } 
        EventAppended = Observable.Empty()
    }
