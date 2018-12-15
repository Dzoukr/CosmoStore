# CosmoStore

<p align="center">
<img src="https://github.com/Dzoukr/CosmoStore/raw/master/logo.png" width="150px"/>
</p>

F# Event store library for Azure Cosmos DB & Azure Table Storage

## Features

- Support for Azure Cosmos DB
- Support for Azure Table Storage
- Optimistic concurrency
- ACID compliant
- Storage agnostic F# API
- Simple Stream querying


## Installation
First install NuGet package [![NuGet](https://img.shields.io/nuget/v/CosmoStore.svg?style=flat)](https://www.nuget.org/packages/CosmoStore/)

    Install-Package CosmoStore

or using [Paket](http://fsprojects.github.io/Paket/getting-started.html)

    nuget CosmoStore

## Event store

Event store (defined as F# record) is by design *storage agnostic* which means that no matter if you use Cosmos DB or Table Storage, the API is the same.

```fsharp
type EventStore = {
    AppendEvent : string -> ExpectedPosition -> EventWrite -> Task<EventRead>
    AppendEvents : string -> ExpectedPosition -> EventWrite list -> Task<EventRead list>
    GetEvent : string -> int64 -> Task<EventRead>
    GetEvents : string -> EventsReadRange -> Task<EventRead list>
    GetStreams : StreamsReadFilter -> Task<Stream list>
    EventAppended : IObservable<EventRead>
}

```

Each function on record is explained in separate chapter.


## Initializing Event store for Azure Cosmos DB

Cosmos DB Event store has own configuration type that follows some specifics of database like *Request units* throughput or *collection capacity* (fixed, unlimited).

```fsharp
type Capacity =
    | Fixed
    | Unlimited

type Configuration = {
    DatabaseName : string
    ServiceEndpoint : Uri
    AuthKey : string
    Capacity: Capacity
    Throughput: int
}

```

> Note: If you don't know these terms check [official documentation](https://docs.microsoft.com/en-us/azure/cosmos-db/request-units)

Configuration can be created "manually" or use default setup (fixed collection with 400 RU/s)

```fsharp
open CosmoStore

let cosmosDbUrl = Uri "https://mycosmosdburl" // check Keys section on Azure portal
let cosmosAuthKey = "VeryPrivateKeyValue==" // check Keys section on Azure portal
let myConfig = CosmosDb.Configuration.CreateDefault cosmosDbUrl cosmosAuthKey

let eventStore = myConfig |> CosmosDb.EventStore.getEventStore
```


## Initializing Event store for Azure Table Storage

Configuration for Table Storage is much easier since we need only *account name* and *authentication key*.

```fsharp
type StorageAccount =
    | Cloud of accountName:string * authKey:string
    | LocalEmulator

type Configuration = {
    DatabaseName : string
    Account : StorageAccount
}

```

As for Cosmos DB, you can easily create default configuration.

```fsharp
open CosmoStore

let storageAccountName = "myStoreageAccountName" // check Keys section on Azure portal
let storageAuthKey = "VeryPrivateKeyValue==" // check Keys section on Azure portal
let myConfig = TableStorage.Configuration.CreateDefault storageAccountName storageAuthKey

let eventStore = myConfig |> TableStorage.EventStore.getEventStore
```

## Writing Events to Stream

Events are data structures you want to write (append) to some "shelf" also known as *Stream*. Event for writing is defined as this type:

```fsharp
type EventWrite = {
    Id : Guid
    CorrelationId : Guid
    Name : string
    Data : JToken
    Metadata : JToken option
}
```

> Note: Newtonsoft.Json library (.NET industry standard) JToken is used as default type for data and metadata.

When writing Events to some Stream, you usually expect them to be written at some position hence you must specify *optimistic concurrency* strategy. For this purpose the type `ExpectedPosition` exists:

```fsharp
type ExpectedPosition =
    | Any // we don't care
    | NoStream // no event must exist in that stream
    | Exact of int64 // exact position of next event
```

There are two defined functions to write Event to Stream. `AppendEvent` for writing single Event and `AppendEvents` to write more Events.

```fsharp
let expected = ExpectedPosition.NoStream // we are expecting brand new stream
let eventToWrite = ... // get new event to be written
let streamId = "MyAmazingStream"

// writing first event
eventToWrite |> eventStore.AppendEvent streamId expected 

let moreEventsToWrite = ... // get list of another events
let newExpected = ExpectedPosition.Exact 2L // we are expecting next event to be on 2nd position

// writing another N events
moreEventsToWrite |> eventStore.AppendEvents streamId newExpected
```

If everything goes well, you will get back list (in *Task*) of written events (type `EventRead` - explained in next chapter).


## Reading Events from Stream

When reading back Events from Stream, you'll a little bit more information than you wrote:

```fsharp
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
```

You have two options how to read back stored Events. You can read single Event by Position using `GetEvent` function:


```fsharp
// return 2nd Event from Stream
let singleEvent = 2L |> eventStore.GetEvent "MyAmazingStream"
```

Or read list of Events using `GetEvents` function. For such reading you need to specify the *range*:

```fsharp
// return 1st-2nd Event from Stream
let firstTwoEvents = EventsReadRange.PositionRange(1,2) |> eventStore.GetEvents "MyAmazingStream"

// return all events
let allEvents = EventsReadRange.AllEvents |> eventStore.GetEvents "MyAmazingStream"
```

To fully understand what are the possibilities have a look at `EventsReadRange` definition:

```fsharp
type EventsReadRange =
    | AllEvents
    | FromPosition of int64
    | ToPosition of int64
    | PositionRange of fromPosition:int64 * toPosition:int64
```

## Reading Streams from Event store

Each Stream has own metadata:

```fsharp
type Stream = {
    Id : string
    LastPosition : int64
    LastUpdatedUtc: DateTime
}
```

Streams can be queried using `GetStreams` function. The querying works similar way as filtering Events by range, but here you can query Streams by string `Id`:

```fsharp
let allAmazingStream = StreamsReadFilter.StartsWith("MyAmazing") |> eventStore.GetStreams
let allStreams = StreamsReadFilter.AllStream |> eventStore.GetStreams
```

The complete possibilities are defined by `StreamsReadFilter` type:

```fsharp
type StreamsReadFilter =
    | AllStreams
    | StarsWith of string
    | EndsWith of string
    | Contains of string
```

## Observing appended events

Since version 1.4.0 you can observe appended events by hooking to `EventAppended` property `IObservable<EventRead>`. Use of [FSharp.Control.Reactive](https://www.nuget.org/packages/FSharp.Control.Reactive) library is recommended, but not required.


## Known issues (Azure Table Storage only)

Azure Table Storage currently allows only *100 operations* (appends) in one batch. CosmoStore reserves one operation per batch for Stream metadata, so if you want to append more than *99 events* to single Stream, you will get `InvalidOperationException`.
