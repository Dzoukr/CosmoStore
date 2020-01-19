# CosmoStore

<p align="center">
<img src="https://github.com/Dzoukr/CosmoStore/raw/master/logo.png" width="150px"/>
</p>

F# Event Store library for various storage providers (Cosmos DB, Table Storage, Marten, InMemory and LiteDB)

## Features
- Storage agnostic F# API
- Support for Azure Cosmos DB
- Support for Azure Table Storage
- Support for Marten
- Support for In-memory
- Support for LiteDB
- Optimistic concurrency
- ACID compliant
- Simple Stream querying


## Available storage providers

| Storage Provider | Payload type | Package | Version | Author
|---|---|---|---|---|
| none (API definition only) | - | CosmoStore | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.svg?style=flat)](https://www.nuget.org/packages/CosmoStore/) | @dzoukr |
| Azure Cosmos DB | `Newtonsoft.Json` | CosmoStore.CosmosDb | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.CosmosDb.svg?style=flat)](https://www.nuget.org/packages/CosmoStore.CosmosDb/) |@dzoukr |
| Azure Table Storage | `Newtonsoft.Json` | CosmoStore.TableStorage  | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.TableStorage.svg?style=flat)](https://www.nuget.org/packages/CosmoStore.TableStorage/) | @dzoukr |
| InMemory | `Newtonsoft.Json` | CosmoStore.InMemory  | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.InMemory.svg?style=flat)](https://www.nuget.org/packages/CosmoStore.InMemory/) | @kunjee
| Marten | `Newtonsoft.Json` | CosmoStore.Marten  | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.Marten.svg?style=flat)](https://www.nuget.org/packages/CosmoStore.Marten/) | @kunjee
| LiteDB | `BsonValue` / `BsonDocument` | CosmoStore.LiteDb  | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.LiteDb.svg?style=flat)](https://www.nuget.org/packages/CosmoStore.LiteDb/) | @kunjee
| ServiceStack | `'a` | CosmoStore.ServiceStack  | [![NuGet](https://img.shields.io/nuget/v/CosmoStore.ServiceStack.svg?style=flat)](https://www.nuget.org/packages/CosmoStore.ServiceStack/) | @kunjee

## What is new in version 3

All previous version of CosmoStore were tightly connected with `Newtonsoft.Json` library and used its `JToken` as default payload for events. Since version 3.0 this does not apply anymore.
Whole definition of `EventStore` was rewritten to be fully generic on payload and also on version level. Why? Some libraries not only use different payload than `JToken`, but possibly use
different type for `Version` then `int64` (default before version 3). Authors of libraries using `CosmoStore` API now can use any payload and any version type that fits best their storage mechanism.

## Event store

Event store (defined as F# record) is by design *storage agnostic* which means that no matter if you use Cosmos DB or Table Storage, the API is the same.

```fsharp
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
```

Each function on record is explained in separate chapter.


## Initializing Event store for Azure Cosmos DB

Cosmos DB Event store has own configuration type that follows some specifics of database like *Request units* throughput.

```fsharp
type Configuration = {
    DatabaseName : string
    ContainerName : string
    ConnectionString : string
    Throughput : int
    InitializeContainer : bool
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
type EventWrite<'payload> = {
    Id : Guid
    CorrelationId : Guid option
    CausationId : Guid option
    Name : string
    Data : 'payload
    Metadata : 'payload option
}
```

When writing Events to some Stream, you usually expect them to be written having some version hence you must specify *optimistic concurrency* strategy. For this purpose the type `ExpectedVersion` exists:

```fsharp
type ExpectedVersion<'version> =
    | Any
    | NoStream
    | Exact of 'version
```

There are two defined functions to write Event to Stream. `AppendEvent` for writing single Event and `AppendEvents` to write more Events.

```fsharp
let expected = ExpectedVersion.NoStream // we are expecting brand new stream
let eventToWrite = ... // get new event to be written
let streamId = "MyAmazingStream"

// writing first event
eventToWrite |> eventStore.AppendEvent streamId expected 

let moreEventsToWrite = ... // get list of another events
let newExpected = ExpectedVersion.Exact 2L // we are expecting next event to be in 2nd version

// writing another N events
moreEventsToWrite |> eventStore.AppendEvents streamId newExpected
```

If everything goes well, you will get back list (in *Task*) of written events (type `EventRead` - explained in next chapter).


## Reading Events from Stream

When reading back Events from Stream, you'll a little bit more information than you wrote:

```fsharp
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
```

You have two options how to read back stored Events. You can read single Event by Version using `GetEvent` function:


```fsharp
// return 2nd Event from Stream
let singleEvent = 2L |> eventStore.GetEvent "MyAmazingStream"
```

Or read list of Events using `GetEvents` function. For such reading you need to specify the *range*:

```fsharp
// return 1st-2nd Event from Stream
let firstTwoEvents = EventsReadRange.VersionRange(1,2) |> eventStore.GetEvents "MyAmazingStream"

// return all events
let allEvents = EventsReadRange.AllEvents |> eventStore.GetEvents "MyAmazingStream"
```

To fully understand what are the possibilities have a look at `EventsReadRange` definition:

```fsharp
type EventsReadRange<'version> =
    | AllEvents
    | FromVersion of 'version
    | ToVersion of 'version
    | VersionRange of fromVersion:'version * toVersion:'version
```

If you are interested in Events based on stored `CorrelationId`, you can use function introduced in version 2 - `GetEventsByCorrelationId`

```fsharp
let myCorrelationId = ... // Guid value
let correlatedEvents = myCorrelationId |> eventStore.GetEventsByCorrelationId
```

## Reading Streams from Event store

Each Stream has own metadata:

```fsharp
type Stream = {
    Id : string
    LastVersion : int64
    LastUpdatedUtc : DateTime
}
```

If you know exact value of Stream `Id`, you can use function `GetStream`. To query more Streams, use `GetStreams` function. The querying works similar way as filtering Events by range, but here you can query Streams by `Id`:

```fsharp
let allAmazingStream = StreamsReadFilter.StartsWith("MyAmazing") |> eventStore.GetStreams
let allStreams = StreamsReadFilter.AllStream |> eventStore.GetStreams
```

The complete possibilities are defined by `StreamsReadFilter` type:

```fsharp
type StreamsReadFilter =
    | AllStreams
    | StartsWith of string
    | EndsWith of string
    | Contains of string
```

## Observing appended events

Since version 1.4.0 you can observe appended events by hooking to `EventAppended` property `IObservable<EventRead>`. Use of [FSharp.Control.Reactive](https://www.nuget.org/packages/FSharp.Control.Reactive) library is recommended, but not required.


## Known issues (Azure Table Storage only)

Azure Table Storage currently allows only *100 operations* (appends) in one batch. CosmoStore reserves one operation per batch for Stream metadata, so if you want to append more than *99 events* to single Stream, you will get `InvalidOperationException`.