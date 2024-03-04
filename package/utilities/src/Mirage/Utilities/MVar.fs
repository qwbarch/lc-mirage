module Mirage.Utilities.MVar
open System.Collections.Generic
open FSharpx.Control

type MVarMessage<'T> =
    | PutMVar of 'T * AsyncReplyChannel<unit>
    | TakeMVar of AsyncReplyChannel<'T>
    | ReadMVar of AsyncReplyChannel<'T>
    | IsEmptyMVar of AsyncReplyChannel<bool>
    | TryReadMVar of AsyncReplyChannel<'T option>
    | TryPutMVar of 'T * AsyncReplyChannel<bool>

// Where operations are defined, follows the same semantics as 
// https://hackage.haskell.org/package/base-4.19.0.0/docs/Control-Concurrent-MVar.html

// WARNING: this MVar is backed by a MailboxProcessor.
// If a thread is waiting for the mailbox with a PostAnd(Async)Reply and the thread has its cancellation token cancelled,
// there is a bug where the thread will be kept alive and is stuck waiting for the reply, unless the mailbox is also killed.
// Solution: Either avoid this pattern or make sure that the mailbox is also killed at a similar time as the thread.
// https://github.com/dotnet/fsharp/issues/6285

type MVar<'T> = AutoCancelAgent<MVarMessage<'T>>
let createEmptyMVar<'T> () : MVar<'T> =
    AutoCancelAgent<MVarMessage<'T>>.Start(fun inbox ->
        let mutable value : 'T option = None
        let readers = List<AsyncReplyChannel<'T>>()
        let writersQueue = Queue<'T * AsyncReplyChannel<unit>>()
        let takersQueue = Queue<AsyncReplyChannel<'T>>()
        let rec loop () =
            async {
                let! message = inbox.Receive()
                match message with
                | PutMVar (newValue, replyChannel) ->
                    match value with
                    | None -> 
                        value <- Some newValue
                        replyChannel.Reply()
                    | Some _ -> writersQueue.Enqueue((newValue, replyChannel))
                | TakeMVar replyChannel -> takersQueue.Enqueue(replyChannel)
                | ReadMVar replyChannel -> readers.Add(replyChannel)
                | IsEmptyMVar replyChannel -> replyChannel.Reply(value.IsNone)
                | TryReadMVar replyChannel -> replyChannel.Reply(value)
                | TryPutMVar (newValue, replyChannel) ->
                    match value with
                    | None ->
                        value <- Some newValue
                        replyChannel.Reply(true)
                    | Some _ -> replyChannel.Reply(false)

                // Consume all outstanding readers if possible
                if value.IsSome then
                    for reader in readers do
                        reader.Reply(value.Value)
                    readers.Clear()

                let mutable cont = true
                while cont do
                    let mutable writeValue = false
                    let mutable takeValue = false
                    match value with
                    | None -> writeValue <- writersQueue.Count > 0
                    | Some _ -> takeValue <- takersQueue.Count > 0

                    if not (writeValue || takeValue) then
                        cont <- false

                    if writeValue then
                        let (writeVal, replyChannel) = writersQueue.Dequeue()
                        value <- Some writeVal
                        replyChannel.Reply()
                    elif takeValue then
                        let replyChannel = takersQueue.Dequeue()
                        replyChannel.Reply(value.Value)
                        value <- None

                do! loop()
            }
        loop()
    )

let putMVar (mvar: MVar<'T>) (a: 'T) = mvar.PostAndAsyncReply(fun rc -> PutMVar (a, rc))

let takeMVar (mvar: MVar<'T>) = mvar.PostAndAsyncReply(fun rc -> TakeMVar rc)

let readMVar (mvar: MVar<'T>) = mvar.PostAndAsyncReply(fun rc -> ReadMVar rc)

let isEmptyMVar (mvar: MVar<'T>) = mvar.PostAndReply(fun rc -> IsEmptyMVar rc)

let tryReadMVar (mvar: MVar<'T>) = mvar.PostAndReply(fun rc -> TryReadMVar rc)

let tryPutMVar (mvar: MVar<'T>) (a: 'T) = mvar.PostAndReply(fun rc -> TryPutMVar (a, rc))