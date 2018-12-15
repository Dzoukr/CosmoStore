module CosmoStore.Tests.Observable

open NUnit.Framework
open CosmoStore
open CosmoStore.Tests.BasicTests
open FSharp.Control.Reactive

[<Test>]
let ``Observers don't interfere with each other`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getCleanEventStore
    let mutable complete1 = false
    let mutable complete2 = false
    let mutable count = 0
    let mutable subThreadNum = 0

    let watch = System.Diagnostics.Stopwatch.StartNew()

    let streamId = getStreamId()
    let events = [1..10] |> List.map getEvent
    
    let mainThreadNum = System.Threading.Thread.CurrentThread.ManagedThreadId

    store.EventAppended 
    |> Observable.add (fun x -> 
        subThreadNum <- System.Threading.Thread.CurrentThread.ManagedThreadId
        complete1 <- true
        System.Threading.Thread.Sleep 50000
        ()
    )
    
    store.EventAppended 
    |> Observable.bufferCount 10
    |> Observable.add (fun x -> 
        count <- x.Count
        complete2 <- true
    )
    
    store.AppendEvents streamId ExpectedPosition.Any events |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    while (complete1 = false || complete2 = false) do ()
    watch.Stop()
    Assert.AreEqual(10, count)
    Assert.AreNotEqual(mainThreadNum, subThreadNum)
    Assert.IsTrue(watch.ElapsedMilliseconds < 10000L)


[<Test>]
let ``Observes appended single event`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getCleanEventStore
    let mutable complete = false
    let mutable count = 0
    let streamId = getStreamId()
    let event = 1 |> getEvent
    store.EventAppended 
    |> Observable.bufferCount 1
    |> Observable.add (fun x -> 
        count <- x.Count
        complete <- true
    )
    
    store.AppendEvent streamId ExpectedPosition.Any event |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    while (complete = false) do ()
    Assert.AreEqual(1, count)
    
[<Test>]
let ``Observes appended events`` ([<Values(StoreType.CosmosDB, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getCleanEventStore
    let mutable complete = false
    let mutable count = 0
    let streamId = getStreamId()
    let events = [1..10] |> List.map getEvent
    store.EventAppended 
    |> Observable.bufferCount 10
    |> Observable.add (fun x -> 
        count <- x.Count
        complete <- true
    )
    
    store.AppendEvents streamId ExpectedPosition.Any events |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    while (complete = false) do ()
    Assert.AreEqual(10, count)