module Mirage.Utilities.Lock
open System.Collections.Generic
open System
open System.Threading

type Lock = SemaphoreSlim
let createLock () : Lock = new SemaphoreSlim(1)

// Returns a key to send back to release the lock.
// It is written in this way to catch bugs with thread A acquiring a lock and thread B tries to release the lock that thread A acquired.
let lockAcquire (lock: Lock) : Async<unit> = Async.AwaitTask(lock.WaitAsync())

let lockRelease (lock: Lock) = ignore <| lock.Release()

type LockContext =
    {   lock: Lock
    }
    
    interface IDisposable with
        member this.Dispose() =
            lockRelease this.lock

let withLock (lock: Lock) : Async<LockContext> = 
    async {
        do! lockAcquire lock
        return { lock = lock; }
    }