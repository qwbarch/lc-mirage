module Mirage.Utilities.BatchProcessor
open FSharpx.Control
open System.Collections.Generic
open FSharpPlus

// Take a function that processes batches and create a BatchProcessor out of it.
// Argument: processBatch: List<'T> -> List<'V>
// This batch processor can then be sent through the getProcessSingle function to get out a function that processes single values
// getProcessSingle (createBatchProcessor processBatch) : 'T -> Async<'V>
// Remember to dispose of the BatchProcessor if necessary.
type BatchProcessor<'T, 'V> = AutoCancelAgent<'T * AsyncReplyChannel<'V>>

let createBatchProcessor (processBatch: List<'T> -> List<'V>) = 
    BatchProcessor<'T, 'V>.Start(fun inbox ->
        let rec loop () =
            async {
                // Block until we get our first message, then keep getting messages until the queue is empty
                let messages = List<'T * AsyncReplyChannel<'V>>()
                let! firstMessage = inbox.Receive()
                messages.Add(firstMessage)
                while inbox.CurrentQueueLength > 0 do
                    let! message = inbox.Receive()
                    messages.Add(message)

                let (inputs: List<'T>, replyChannels: List<AsyncReplyChannel<'V>>) = unzip messages
                let outputs: List<'V> = processBatch inputs
                for (output: 'V, replyChannel: AsyncReplyChannel<'V>) in zip outputs replyChannels do
                    replyChannel.Reply(output)

                do! loop()
            }
        loop()
    )

let getProcessSingle<'T, 'V>(batchProcessor: BatchProcessor<'T, 'V>) (input: 'T) =
    batchProcessor.PostAndAsyncReply((fun replyChannel -> (input, replyChannel)))
