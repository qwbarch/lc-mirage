module Mirage.Utilities.LVar
open Mirage.Utilities.Lock
open System

// A locked variable
type LVar<'T> =
    private {   
        lock: Lock
        mutable value: 'T
    }
    interface IDisposable with
        member this.Dispose() =
            this.lock.Dispose()

let newLVar (value: 'T) = { lock = createLock(); value = value }

// Be mindful that 'T could be a reference and the underlying data can still be modified through it.
// Create a copy of the data with accessLVar instead if necessary.
let readLVar (lvar: LVar<'T>) =
    async {
        use! __ = withLock lvar.lock
        return lvar.value
    }

// Writes a new value into the LVar and returns the previous value
let writeLVar (lvar: LVar<'T>) (newValue: 'T) =
    async {
        use! __ = withLock lvar.lock
        let oldValue = lvar.value
        lvar.value <- newValue
        return oldValue
    }

let accessLVar (lvar: LVar<'T>) (f : 'T -> 'V) =
    async {
        use! __ = withLock lvar.lock
        return f lvar.value
    }

let modifyLVar (lvar: LVar<'T>) (f : 'T -> 'T) =
    async {
        use! __ = withLock lvar.lock
        lvar.value <- f lvar.value
        ()
    }
