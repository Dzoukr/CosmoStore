module CosmoStore.Tests.BasicTests

open System
open CosmoStore
open Domain
open Expecto
open Domain.ExpectoHelpers
open System.Threading.Tasks

let private withCorrelationId i (e:EventWrite<_>) = { e with CorrelationId = Some i }

let eventsTests (cfg:TestConfiguration<_,_>) = 
    testList "Events" [
        
        // testTask "Append events parallel" {
        //     let streamId = cfg.GetStreamId()
            
        //     let storeEvent = async {
        //         return! 
        //             [1..10] 
        //             |> List.map cfg.GetEvent 
        //             |> cfg.Store.AppendEvents streamId Any
        //             |> Async.AwaitTask
        //     }
            
        //     [1..10]
        //     |> List.map (fun _ -> storeEvent)
        //     |> Async.Parallel
        //     |> Async.RunSynchronously
        //     |> ignore
        // }

        // testTask "Store same event twice" {
        //     let streamId = cfg.GetStreamId()
        //     let event = cfg.GetEvent 0
        //     for _ in 1..2 do
        //         do! event |> cfg.Store.AppendEvent streamId Any
        // }

        testTask "Appends event" {
            let streamId = cfg.GetStreamId()
            let! e = cfg.GetEvent 0 |> cfg.Store.AppendEvent streamId Any
            equal e.Position 1L
        }

        testTask "Append 100 events" {
            let streamId = cfg.GetStreamId()
            
            let! events = [1..99] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId ExpectedPosition.Any
            areAscending events
            areNewer events
        }
        
        testTask "Gets event" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! event = cfg.Store.GetEvent streamId 3L
            equal event.Position 3L
            equal event.Name "Created_3"
        }

        testTask "Get events (all)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = cfg.Store.GetEvents streamId EventsReadRange.AllEvents
            equal 10 events.Length
            areAscending events
        }

        testTask "Get events (from position)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = cfg.Store.GetEvents streamId (EventsReadRange.FromPosition(6L))
            equal 5 events.Length
            areAscending events 
        }
        
        testTask "Get events (to position)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = cfg.Store.GetEvents streamId (EventsReadRange.ToPosition(5L))
            equal 5 events.Length
            areAscending events 
        }
        
        testTask "Get events (position range)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = cfg.Store.GetEvents streamId (EventsReadRange.PositionRange(5L,7L))
            equal 3 events.Length
            areAscending events 
            equal 5L events.Head.Position
        }

        testTask "Fails to append to existing position" {
            Expect.throwsC (fun _ -> 
                let streamId = cfg.GetStreamId()
                do cfg.GetEvent 1 |> cfg.Store.AppendEvent streamId ExpectedPosition.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
                do cfg.GetEvent 1 |> cfg.Store.AppendEvent streamId (ExpectedPosition.Exact(1L)) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            ) (fun ex -> 
                isTrue <| ex.Message.Contains("ESERROR_POSITION_POSITIONNOTMATCH")
            )
        }

        testTask "Fails to append to existing stream if is not expected to exist" {
            Expect.throwsC (fun _ -> 
                let streamId = cfg.GetStreamId()
                do cfg.GetEvent 1 |> cfg.Store.AppendEvent streamId ExpectedPosition.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
                do cfg.GetEvent 1 |> cfg.Store.AppendEvent streamId ExpectedPosition.NoStream |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            ) (fun ex -> 
                isTrue <| ex.Message.Contains("ESERROR_POSITION_STREAMEXISTS")
            )
        }

        testTask "Appending no events does not affect stream metadata" {
            let streamId = cfg.GetStreamId()
            // append single event
            do! 0 |> cfg.GetEvent |> cfg.Store.AppendEvent streamId (ExpectedPosition.Exact(1L))
            let! stream = cfg.Store.GetStream streamId
            do! List.empty |> cfg.Store.AppendEvents streamId ExpectedPosition.Any
            let! streamAfterAppend = cfg.Store.GetStream streamId
            equal stream streamAfterAppend
        }

        testTask "Appending 1000 events can be read back" {
            let streamId = cfg.GetStreamId()
        
            [0..999]
            |> List.map cfg.GetEvent
            |> List.chunkBySize 99
            |> List.iter (fun evns -> 
                evns |> cfg.Store.AppendEvents streamId ExpectedPosition.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            )

            let! (stream : Stream<_>) = cfg.Store.GetStream streamId
            equal 1000L stream.LastPosition

            let! (evntsBack : EventRead<_,_> list) = cfg.Store.GetEvents streamId EventsReadRange.AllEvents
            equal 1000 evntsBack.Length
        }

        testTask "Can read events by correlation ID" {
            let addEventToStream corrId i =
                [1..10] 
                |> List.map cfg.GetEvent 
                |> List.map (withCorrelationId corrId)
                |> cfg.Store.AppendEvents (sprintf "CORR_%i" i) ExpectedPosition.Any
            
            let corrId = Guid.NewGuid()
            for i in 1..3 do
                do! addEventToStream corrId i
            
            let differentCorrId = Guid.NewGuid()
            for i in 1..3 do
                do! addEventToStream differentCorrId i

            let! (events : EventRead<_,_> list) = cfg.Store.GetEventsByCorrelationId corrId
            let uniqueStreams = events |> List.map (fun x -> x.StreamId) |> List.distinct |> List.sort
            equal 30 events.Length
            equal ["CORR_1";"CORR_2";"CORR_3"] uniqueStreams
        }
    ]

let streamsTestsSequenced (cfg:TestConfiguration<_,_>) =
    testList "Streams" [
        
        testTask "Get streams (all)" {
            
            let store = cfg.GetEmptyStore()

            let addEventToStream i =
                [1..99] 
                |> List.map cfg.GetEvent 
                |> store.AppendEvents (sprintf "A_%i" i) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = store.GetStreams StreamsReadFilter.AllStreams
            equal ["A_1";"A_2";"A_3"] (streams |> List.map (fun x -> x.Id))
            equal 99L streams.Head.LastPosition
            isTrue (streams.Head.LastUpdatedUtc > DateTime.MinValue)
        }
        
        testTask "Get streams (startswith)" {
            
            let store = cfg.GetEmptyStore()
            
            let name = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                [1..99] 
                |> List.map cfg.GetEvent 
                |> store.AppendEvents (sprintf "X%i_%s" i name) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = store.GetStreams (StreamsReadFilter.StartsWith("X2_"))
            equal ["X2_"+name] (streams |> List.map (fun x -> x.Id))
            equal 1 streams.Length
        }
    ] |> testSequenced

let streamsTests (cfg:TestConfiguration<_,_>) =
    testList "Streams" [
        
        testTask "Get streams (endswith)" {
            let endsWith = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                cfg.GetEvent 1
                |> cfg.Store.AppendEvent (sprintf "X%i_%s" i endsWith) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = cfg.Store.GetStreams (StreamsReadFilter.EndsWith(endsWith))
            equal 3 streams.Length
        }
        
        testTask "Get streams (contains)" {
            let contains = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                cfg.GetEvent 1
                |> cfg.Store.AppendEvent (sprintf "C_%s_%i" contains i) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = cfg.Store.GetStreams (StreamsReadFilter.Contains(contains))
            equal 3 streams.Length
            equal (sprintf "C_%s_1" contains) streams.Head.Id
        }

        testTask "Get stream" {
            let streamId = (sprintf "OS_%s" (Guid.NewGuid().ToString("N")))
            do! [1..10]
                |> List.map cfg.GetEvent
                |> cfg.Store.AppendEvents streamId ExpectedPosition.Any
            let! stream = cfg.Store.GetStream streamId
            equal stream.LastPosition 10L
            equal stream.Id streamId
        }
    ]

let allTests =
    [
        eventsTests
        streamsTestsSequenced
        streamsTests
    ]