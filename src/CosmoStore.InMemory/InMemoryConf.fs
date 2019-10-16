namespace CosmoStore.InMemory
open System
open System.Collections.Concurrent
open CosmoStore

type StreamStoreType<'position> = ConcurrentDictionary<string, Stream<'position>>
type EventStoreType<'payload, 'position> = ConcurrentDictionary<Guid, EventRead<'payload, 'position>>

type Configuration<'payload, 'position> = {
    InMemoryStreams : StreamStoreType<'position>
    InMemoryEvents :  EventStoreType<'payload, 'position>
}