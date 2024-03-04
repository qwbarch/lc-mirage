module Mirage.Utilities.Async

open FSharpPlus
open FSharpx.Control

/// <summary>
/// Run the given program from the async thread pool, and then return the value to the caller thread.
/// </summary>
let forkReturn<'A> (program: Async<'A>) : Async<'A> =
    async {
        let agent = new BlockingQueueAgent<'A>(1)
        Async.Start(agent.AsyncAdd =<< program)
        return! agent.AsyncGet()
    }