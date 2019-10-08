module CosmoStore.Tests.BasicTests

open System
open CosmoStore
open Domain
open Expecto
open Domain.ExpectoHelpers
open System.Threading.Tasks

let private withCorrelationId i (e:EventWrite<_>) = { e with CorrelationId = Some i }

let eventsTests (gen:TestDataGenerator<_>) eventStore = 
    testList "Events" [
        
        // testTask "Append events parallel" {
        //     let streamId = cfg.GetStreamId()
            
        //     let storeEvent = async {
        //         return! 
        //             [1..10] 
        //             |> List.map cfg.GetEvent 
        //             |> eventStore.AppendEvents streamId Any
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
        //         do! event |> eventStore.AppendEvent streamId Any
        // }

        testTask "Appends event" {
            let streamId = gen.GetStreamId()
            let! e = gen.GetEvent 0 |> eventStore.AppendEvent streamId Any
            equal e.Version 1L
        }

        testTask "Append 100 events" {
            let streamId = gen.GetStreamId()
            
            let! events = [1..99] |> List.map gen.GetEvent |> eventStore.AppendEvents streamId ExpectedVersion.Any
            areAscending events
            areNewer events
        }
        
        testTask "Gets event" {
            let streamId = gen.GetStreamId()
            do! [1..10] |> List.map gen.GetEvent |> eventStore.AppendEvents streamId Any
            let! event = eventStore.GetEvent streamId 3L
            equal event.Version 3L
            equal event.Name "Created_3"
        }

        testTask "Get events (all)" {
            let streamId = gen.GetStreamId()
            do! [1..10] |> List.map gen.GetEvent |> eventStore.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = eventStore.GetEvents streamId EventsReadRange.AllEvents
            equal 10 events.Length
            areAscending events
        }

        testTask "Get events (from position)" {
            let streamId = gen.GetStreamId()
            do! [1..10] |> List.map gen.GetEvent |> eventStore.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = eventStore.GetEvents streamId (EventsReadRange.FromVersion(6L))
            equal 5 events.Length
            areAscending events 
        }
        
        testTask "Get events (to position)" {
            let streamId = gen.GetStreamId()
            do! [1..10] |> List.map gen.GetEvent |> eventStore.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = eventStore.GetEvents streamId (EventsReadRange.ToVersion(5L))
            equal 5 events.Length
            areAscending events 
        }
        
        testTask "Get events (position range)" {
            let streamId = gen.GetStreamId()
            do! [1..10] |> List.map gen.GetEvent |> eventStore.AppendEvents streamId Any
            let! (events : EventRead<_,_> list) = eventStore.GetEvents streamId (EventsReadRange.VersionRange(5L,7L))
            equal 3 events.Length
            areAscending events 
            equal 5L events.Head.Version
        }

        testTask "Fails to append to existing position" {
            Expect.throwsC (fun _ -> 
                let streamId = gen.GetStreamId()
                do gen.GetEvent 1 |> eventStore.AppendEvent streamId ExpectedVersion.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
                do gen.GetEvent 1 |> eventStore.AppendEvent streamId (ExpectedVersion.Exact(1L)) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            ) (fun ex -> 
                (ex.Message.Contains("ESERROR_POSITION_POSITIONNOTMATCH") || ex.Message.Contains("ESERROR_VERSION_VERSIONNOTMATCH"))
                |> isTrue
            )
        }

        testTask "Fails to append to existing stream if is not expected to exist" {
            Expect.throwsC (fun _ -> 
                let streamId = gen.GetStreamId()
                do gen.GetEvent 1 |> eventStore.AppendEvent streamId ExpectedVersion.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
                do gen.GetEvent 1 |> eventStore.AppendEvent streamId ExpectedVersion.NoStream |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            ) (fun ex -> 
                (ex.Message.Contains("ESERROR_POSITION_STREAMEXISTS") || ex.Message.Contains("ESERROR_VERSION_STREAMEXISTS"))
                |> isTrue
            )
        }

        testTask "Appending no events does not affect stream metadata" {
            let streamId = gen.GetStreamId()
            // append single event
            do! 0 |> gen.GetEvent |> eventStore.AppendEvent streamId (ExpectedVersion.Exact(1L))
            let! stream = eventStore.GetStream streamId
            do! List.empty |> eventStore.AppendEvents streamId ExpectedVersion.Any
            let! streamAfterAppend = eventStore.GetStream streamId
            equal stream streamAfterAppend
        }

        testTask "Appending 1000 events can be read back" {
            let streamId = gen.GetStreamId()
        
            [0..999]
            |> List.map gen.GetEvent
            |> List.chunkBySize 99
            |> List.iter (fun evns -> 
                evns |> eventStore.AppendEvents streamId ExpectedVersion.Any |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            )

            let! (stream : Stream<_>) = eventStore.GetStream streamId
            equal 1000L stream.LastVersion

            let! (evntsBack : EventRead<_,_> list) = eventStore.GetEvents streamId EventsReadRange.AllEvents
            equal 1000 evntsBack.Length
        }

        testTask "Can read events by correlation ID" {
            let addEventToStream corrId i =
                [1..10] 
                |> List.map gen.GetEvent 
                |> List.map (withCorrelationId corrId)
                |> eventStore.AppendEvents (sprintf "CORR_%i" i) ExpectedVersion.Any
            
            let corrId = Guid.NewGuid()
            for i in 1..3 do
                do! addEventToStream corrId i
            
            let differentCorrId = Guid.NewGuid()
            for i in 1..3 do
                do! addEventToStream differentCorrId i

            let! (events : EventRead<_,_> list) = eventStore.GetEventsByCorrelationId corrId
            let uniqueStreams = events |> List.map (fun x -> x.StreamId) |> List.distinct |> List.sort
            equal 30 events.Length
            equal ["CORR_1";"CORR_2";"CORR_3"] uniqueStreams
        }
    ]

let streamsTests (gen:TestDataGenerator<_>) eventStore =
    testList "Streams" [
        
        testTask "Get streams (all)" {
            let prefix = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                [1..99] 
                |> List.map gen.GetEvent 
                |> eventStore.AppendEvents (sprintf "%s_%i" prefix i) ExpectedVersion.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = eventStore.GetStreams StreamsReadFilter.AllStreams
            Expect.containsAll (streams |> List.map (fun x -> x.Id)) [prefix + "_1"; prefix + "_2"; prefix + "_3"] "" 
            equal 99L (streams |> List.filter (fun x -> x.Id.StartsWith(prefix)) |> List.head |> (fun x -> x.LastVersion))
            isTrue (streams.Head.LastUpdatedUtc > DateTime.MinValue)
        }
        
        testTask "Get streams (startswith)" {
            let prefix = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                [1..99] 
                |> List.map gen.GetEvent 
                |> eventStore.AppendEvents (sprintf "%s_%i" prefix i) ExpectedVersion.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = eventStore.GetStreams (StreamsReadFilter.StartsWith(prefix + "_2"))
            equal [prefix+"_2"] (streams |> List.map (fun x -> x.Id))
            equal 1 streams.Length
        }
        
        testTask "Get streams (endswith)" {
            let endsWith = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                gen.GetEvent 1
                |> eventStore.AppendEvent (sprintf "X%i_%s" i endsWith) ExpectedVersion.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = eventStore.GetStreams (StreamsReadFilter.EndsWith(endsWith))
            equal 3 streams.Length
        }
        
        testTask "Get streams (contains)" {
            let contains = Guid.NewGuid().ToString("N")
            let addEventToStream i =
                gen.GetEvent 1
                |> eventStore.AppendEvent (sprintf "C_%s_%i" contains i) ExpectedVersion.Any
            
            for i in 1..3 do
                do! addEventToStream i    
            let! (streams : Stream<_> list) = eventStore.GetStreams (StreamsReadFilter.Contains(contains))
            equal 3 streams.Length
            equal (sprintf "C_%s_1" contains) streams.Head.Id
        }

        testTask "Get stream" {
            let streamId = (sprintf "OS_%s" (Guid.NewGuid().ToString("N")))
            do! [1..10]
                |> List.map gen.GetEvent
                |> eventStore.AppendEvents streamId ExpectedVersion.Any
            let! stream = eventStore.GetStream streamId
            equal stream.LastVersion 10L
            equal stream.Id streamId
        }
    ]

let allTests generator eventStore =
    [
        eventsTests generator eventStore
        streamsTests generator eventStore
    ]