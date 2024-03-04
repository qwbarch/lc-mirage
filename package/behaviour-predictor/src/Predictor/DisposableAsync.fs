module Predictor.DisposableAsync
open System.Threading
open System

type DisposableAsync =
    {   tokenSource: CancellationTokenSource
    }
    interface IDisposable with
        member this.Dispose() =
            this.tokenSource.Cancel()
            this.tokenSource.Dispose()

let startAsyncAsDisposable (f : Async<unit>) : DisposableAsync =
    let tokenSource = new CancellationTokenSource()
    Async.Start (f, tokenSource.Token)
    { tokenSource = tokenSource }