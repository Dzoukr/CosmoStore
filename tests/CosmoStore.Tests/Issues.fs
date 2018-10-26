module CosmoStore.Tests.Issues

open NUnit.Framework
open CosmoStore
open CosmoStore.Tests.BasicTests

[<Test>]
let ``Can read back Events stored without metadata`` ([<Values(StoreType.CosmosSmall, StoreType.CosmosBig, StoreType.TableStorage)>] (typ:StoreType)) =
    let store = typ |> getCleanEventStore
    let streamId = getStreamId()
    let event = 
        1 |> getEvent |> fun e -> { e with Metadata = None }
    event
    |> store.AppendEvent streamId ExpectedPosition.Any
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> fun e ->
        Assert.AreEqual(None, e.Metadata)
    |> ignore