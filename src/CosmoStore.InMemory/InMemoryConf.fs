namespace CosmoStore.InMemory
open System
open System.Collections.Concurrent
open CosmoStore

type StreamStoreType = ConcurrentDictionary<string, Stream>
type EventStoreType = ConcurrentDictionary<Guid, EventRead>

type Configuration = {
    InMemoryStreams : StreamStoreType
    InMemoryEvents :  EventStoreType
}