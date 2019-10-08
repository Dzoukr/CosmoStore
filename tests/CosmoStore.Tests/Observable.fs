module CosmoStore.Tests.Observable

open Domain
open CosmoStore
open FSharp.Control.Reactive
open Expecto
open ExpectoHelpers

let allTests (gen:TestDataGenerator<_>) eventStore = 
    [
        testTask "Observers don't interfere with each other" {
            let mutable complete1 = false
            let mutable complete2 = false
            let mutable count = 0
            let mutable subThreadNum = 0

            let watch = System.Diagnostics.Stopwatch.StartNew()

            let streamId = gen.GetStreamId()
            let events = [1..10] |> List.map gen.GetEvent
    
            let mainThreadNum = System.Threading.Thread.CurrentThread.ManagedThreadId

            eventStore.EventAppended 
            |> Observable.add (fun x -> 
                subThreadNum <- System.Threading.Thread.CurrentThread.ManagedThreadId
                complete1 <- true
                System.Threading.Thread.Sleep 50000
                ()
            )
    
            eventStore.EventAppended 
            |> Observable.bufferCount 10
            |> Observable.add (fun x -> 
                count <- x.Count
                complete2 <- true
            )
    
            do! eventStore.AppendEvents streamId ExpectedVersion.Any events 
            while (complete1 = false || complete2 = false) do ()
            watch.Stop()

            equal 10 count
            notEqual mainThreadNum subThreadNum
            isTrue (watch.ElapsedMilliseconds < 10000L)
        }
        
        testTask "Observes appended single event" {
            let mutable complete = false
            let mutable count = 0
            let streamId = gen.GetStreamId()
            let event = 1 |> gen.GetEvent
            eventStore.EventAppended 
            |> Observable.bufferCount 1
            |> Observable.add (fun x -> 
                count <- x.Count
                complete <- true
            )
    
            do! eventStore.AppendEvent streamId ExpectedVersion.Any event
            while (complete = false) do ()
            equal 1 count
        }

        testTask "Observes appended events" {
            let mutable complete = false
            let mutable count = 0
            let streamId = gen.GetStreamId()
            let events = [1..10] |> List.map gen.GetEvent
            eventStore.EventAppended 
            |> Observable.bufferCount 10
            |> Observable.add (fun x -> 
                count <- x.Count
                complete <- true
            )
    
            do! eventStore.AppendEvents streamId ExpectedVersion.Any events
            while (complete = false) do ()
            equal 10 count
        }
    ]