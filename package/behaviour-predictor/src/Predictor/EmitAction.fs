module Predictor.EmitAction
open System.Collections.Concurrent
open Domain
open System
open FSharpx.Control

let createActionEmitter
    (sendMimicText: Guid -> unit) = AutoCancelAgent<FutureAction>.Start(fun inbox ->
        let rec loop () =
            async {
                let! action = inbox.Receive()
                match action with
                | NoAction -> ()
                | QueueAction queueAction ->
                    let delay = queueAction.delay
                    let action = queueAction.action
                    do! Async.Sleep delay
                    sendMimicText action.fileId
                    do! Async.Sleep (action.duration + 10)

                    // Consume actions that accumulated
                    while inbox.CurrentQueueLength > 0 do
                        let! _ = inbox.TryReceive(1)
                        ()
                do! loop()
            }
        loop()
    )