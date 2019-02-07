module CosmoStore.Tests.BasicTests

open System
open CosmoStore
open Expecto
open Domain
open Domain.ExpectoHelpers

let eventsTests (cfg:TestConfiguration) = 
    testList "Events" [
        
        testTask "Appends event" {
            let streamId = cfg.GetStreamId()
            let! e = cfg.GetEvent 0 |> cfg.Store.AppendEvent streamId Any
            equal e.Position 1L
        }

        testTask "Append events" {
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
            let! (events : EventRead list) = cfg.Store.GetEvents streamId EventsReadRange.AllEvents
            equal 10 events.Length
            areAscending events
        }

        testTask "Get events (from position)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead list) = cfg.Store.GetEvents streamId (EventsReadRange.FromPosition(6L))
            equal 5 events.Length
            areAscending events 
        }
        
        testTask "Get events (to position)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead list) = cfg.Store.GetEvents streamId (EventsReadRange.ToPosition(5L))
            equal 5 events.Length
            areAscending events 
        }
        
        testTask "Get events (position range)" {
            let streamId = cfg.GetStreamId()
            do! [1..10] |> List.map cfg.GetEvent |> cfg.Store.AppendEvents streamId Any
            let! (events : EventRead list) = cfg.Store.GetEvents streamId (EventsReadRange.PositionRange(5L,7L))
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

            let! (stream : Stream) = cfg.Store.GetStream streamId
            equal 1000L stream.LastPosition

            let! (evntsBack : EventRead list) = cfg.Store.GetEvents streamId EventsReadRange.AllEvents
            equal 1000 evntsBack.Length
        }
    ]

let streamsTestsSequenced (cfg:TestConfiguration) =
    testList "Streams" [
        
        testTask "Get streams (all)" {
            
            let store = cfg.GetEmptyStore()

            let addEventToStream i =
                [1..99] 
                |> List.map cfg.GetEvent 
                |> store.AppendEvents (sprintf "A_%i" i) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream list) = store.GetStreams StreamsReadFilter.AllStreams
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
            let! (streams : Stream list) = store.GetStreams (StreamsReadFilter.StarsWith("X2_"))
            equal ["X2_"+name] (streams |> List.map (fun x -> x.Id))
            equal 1 streams.Length
        }
    ] |> testSequenced

let streamsTests (cfg:TestConfiguration) =
    testList "Streams" [
        
        testTask "Get streams (endswith)" {
            let endsWith = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                cfg.GetEvent 1
                |> cfg.Store.AppendEvent (sprintf "X%i_%s" i endsWith) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream list) = cfg.Store.GetStreams (StreamsReadFilter.EndsWith(endsWith))
            equal 3 streams.Length
        }
        
        testTask "Get streams (contains)" {
            let contains = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                cfg.GetEvent 1
                |> cfg.Store.AppendEvent (sprintf "C_%s_%i" contains i) ExpectedPosition.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream list) = cfg.Store.GetStreams (StreamsReadFilter.Contains(contains))
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