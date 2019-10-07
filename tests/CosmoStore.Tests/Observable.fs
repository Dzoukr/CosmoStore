module CosmoStore.Tests.Observable

open Domain
open CosmoStore
open FSharp.Control.Reactive
open Expecto
open ExpectoHelpers

let allTests (cfg:TestDataGenerator<_,_>) = 
    [
        testTask "Observers don't interfere with each other" {
            let mutable complete1 = false
            let mutable complete2 = false
            let mutable count = 0
            let mutable subThreadNum = 0

            let watch = System.Diagnostics.Stopwatch.StartNew()

            let streamId = cfg.GetStreamId()
            let events = [1..10] |> List.map cfg.GetEvent
    
            let mainThreadNum = System.Threading.Thread.CurrentThread.ManagedThreadId

            cfg.Store.EventAppended 
            |> Observable.add (fun x -> 
                subThreadNum <- System.Threading.Thread.CurrentThread.ManagedThreadId
                complete1 <- true
                System.Threading.Thread.Sleep 50000
                ()
            )
    
            cfg.Store.EventAppended 
            |> Observable.bufferCount 10
            |> Observable.add (fun x -> 
                count <- x.Count
                complete2 <- true
            )
    
            do! cfg.Store.AppendEvents streamId ExpectedVersion.Any events 
            while (complete1 = false || complete2 = false) do ()
            watch.Stop()

            equal 10 count
            notEqual mainThreadNum subThreadNum
            isTrue (watch.ElapsedMilliseconds < 10000L)
        }
        
        testTask "Observes appended single event" {
            let mutable complete = false
            let mutable count = 0
            let streamId = cfg.GetStreamId()
            let event = 1 |> cfg.GetEvent
            cfg.Store.EventAppended 
            |> Observable.bufferCount 1
            |> Observable.add (fun x -> 
                count <- x.Count
                complete <- true
            )
    
            do! cfg.Store.AppendEvent streamId ExpectedVersion.Any event
            while (complete = false) do ()
            equal 1 count
        }

        testTask "Observes appended events" {
            let mutable complete = false
            let mutable count = 0
            let streamId = cfg.GetStreamId()
            let events = [1..10] |> List.map cfg.GetEvent
            cfg.Store.EventAppended 
            |> Observable.bufferCount 10
            |> Observable.add (fun x -> 
                count <- x.Count
                complete <- true
            )
    
            do! cfg.Store.AppendEvents streamId ExpectedVersion.Any events
            while (complete = false) do ()
            equal 10 count
        }
    ]