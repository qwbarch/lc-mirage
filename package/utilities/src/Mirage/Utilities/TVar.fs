module Mirage.Utilities.TVar

open System
open System.Threading
open System.Collections.Generic

/// <summary>
/// A base type for transactional variables
/// </summary>
[<AbstractClass>]
type TVar() =
    static let nextId = ref 0
    let _id = Interlocked.Increment(nextId)
    member private __.Id = _id
    interface IComparable<TVar> with
        member __.CompareTo(other) = _id.CompareTo(other.Id)

/// <summary>
/// A transactional variable.
/// </summary>
[<Sealed>]
type TVar<'T> internal (value: 'T, cmp: IEqualityComparer<'T>) =
    inherit TVar()
    let mutable _value = value
    member internal __.Value 
        with get () = _value
        and set value = _value <- value
    member internal __.Comparer = cmp

type private IEntry =
    abstract Location : TVar
    abstract IsValid : unit -> bool
    abstract Commit : unit -> unit
    abstract MergeNested : IEntry -> unit

[<Sealed>]
type private Entry<'T> private (location: TVar<'T>, value: 'T, hasOldValue) =
    let _oldValue = location.Value
    let mutable _newValue = value
    new (location, value) = Entry(location, value, false)
    new (location) = Entry(location, location.Value, true)
    member internal __.OldValue = _oldValue
    member internal __.NewValue 
        with get () = _newValue
        and set value = _newValue <- value

    interface IEntry with
        member __.Location = location :> _
        member __.Commit() = location.Value <- _newValue
        member __.MergeNested(entry) = (entry :?> Entry<'T>).NewValue <- _newValue
        member __.IsValid() = not hasOldValue || location.Comparer.Equals( location.Value, _oldValue)

[<Sealed>]
type private ReferenceEqualityComparer<'T when 'T : not struct and 'T : equality>() =
    interface IEqualityComparer<'T> with
        member __.Equals(x, y) = obj.ReferenceEquals(x, y)
        member __.GetHashCode(x) = x.GetHashCode()

[<Sealed>]
type private EquatableEqualityComparer<'T when 'T :> IEquatable<'T> and 'T : struct and 'T : equality>() =
    interface IEqualityComparer<'T> with
        member __.Equals(x, y) = x.Equals(y)
        member __.GetHashCode(x) = x.GetHashCode()

[<Sealed>]
type private AnyEqualityComparer<'T when 'T : equality>() =
    interface IEqualityComparer<'T> with
        member __.Equals(x, y) = x.Equals(y)
        member __.GetHashCode(x) = x.GetHashCode()
        
type private RetryException() =
    inherit Exception()

type private CommitFailedException() =
    inherit Exception()

/// <summary>
/// A transactional memory log.
/// </summary>
[<Sealed; AllowNullLiteral>]
type TLog private (outer) =
    static let locker = obj()
    let log = SortedDictionary<TVar,IEntry>()
    private new () = TLog(null)
    member private __.Log = log
    member private __.Outer = outer

    static member NewTVarClass(value) = TVar<_>(value, ReferenceEqualityComparer())

    static member NewTVarStruct(value) = TVar<_>(value, EquatableEqualityComparer())

    static member NewTVarBoxedStruct(value) = TVar<_>(value, AnyEqualityComparer())

    static member NewTVar(value: 'T) =
        let ty = typeof<'T>
        let ect =
            if not ty.IsValueType then typedefof<ReferenceEqualityComparer<_>>
            elif typeof<IEquatable<'T>>.IsAssignableFrom(ty) then typedefof<EquatableEqualityComparer<_>>
            else typedefof<AnyEqualityComparer<_>>
        let cmp = Activator.CreateInstance(ect.MakeGenericType(ty)) :?> _
        TVar<_>(value, cmp)

    member this.ReadTVar(location) =
        let rec loop (trans: TLog) =
            match trans.Log.TryGetValue(location) with
            | true, (:? Entry<_> as entry) -> entry.NewValue
            | _ -> 
                match trans.Outer with 
                | null -> 
                    let entry = Entry<_>(location)
                    log.Add(location, entry)
                    entry.OldValue
                | outer -> loop outer
        loop this

    member __.WriteTVar(location, value: 'T) =
        match log.TryGetValue(location) with
        | true, (:? Entry<'T> as entry) -> entry.NewValue <- value
        | _ ->
            let entry = Entry<_>(location, value)
            log.Add(location, entry)

    member private __.IsValidSingle() =
        log.Values |> Seq.forall (fun entry -> entry.IsValid())

    member internal this.IsValid() =
        this.IsValidSingle() && (obj.ReferenceEquals(outer, null) || outer.IsValid())

    member internal __.Commit() =
        match outer with
        | null -> for entry in log.Values do entry.Commit()
        | _ -> raise (InvalidOperationException())

    member internal this.StartNested() = TLog(this)

    member internal __.MergeNested() =
        for innerEntry in log.Values do
            match outer.Log.TryGetValue(innerEntry.Location) with
            | true, outerEntry -> innerEntry.MergeNested(outerEntry)
            | _ -> outer.Log.Add(innerEntry.Location, innerEntry)

    member internal __.Wait() = ()
    member internal __.UnWait() = ()
    member private __.Lock() = Monitor.Enter(locker)
    member private __.UnLock() = Monitor.Exit(locker)
    member private __.Block() = Monitor.Wait(locker) |> ignore
    member private __.Signal() = Monitor.PulseAll(locker)

    static member Atomic<'T>(p: TLog -> 'T) =
        let trans = TLog()
        let rec loop() =
            try
                let result = p trans
                trans.Lock()
                let isValid = trans.IsValid()
                if isValid then
                    trans.Commit()
                    trans.Signal()
                trans.UnLock()
                if isValid then result
                else cont()
            with
                | :? RetryException -> retry()
                | :? CommitFailedException
                | :? ThreadInterruptedException -> reraise()
                | _ ->
                    trans.Lock()
                    let isValid = trans.IsValid()
                    trans.UnLock()
                    if isValid then reraise()
                    else cont()
        and cont() =
            trans.Log.Clear()
            Thread.Sleep(0)
            loop()
        and retry() =
            trans.Lock()
            let isValid = trans.IsValid()
            if isValid then
                trans.Wait()
                try
                    let rec loop() =
                        trans.Block()
                        if trans.IsValid() then loop()
                    loop()
                finally
                    trans.UnWait()
                    trans.UnLock()
            else trans.UnLock()
            cont()
        loop()

    static member Atomic(p: TLog -> unit) = TLog.Atomic<_>(p) |> ignore

    member __.Retry() =
        raise (RetryException())

    member this.Retry() = this.Retry() |> ignore

    member this.OrElse<'T>(p: TLog -> 'T, q: TLog -> 'T) =
        let first = this.StartNested()
        try
            let result = p first
            first.Lock()
            let isValid = first.IsValid()
            first.UnLock()
            if isValid then 
                first.MergeNested()
                result
            else
                raise (CommitFailedException())
        with
            | :? RetryException ->
                let second = this.StartNested()
                try
                    let result = q second
                    second.Lock()
                    let isValid = second.IsValid()
                    if isValid then 
                        second.MergeNested()
                        result
                    else
                        raise (CommitFailedException())
                with 
                    | :? RetryException ->
                        this.Lock()
                        let isValid = first.IsValidSingle() && second.IsValidSingle() && this.IsValid()
                        this.UnLock()
                        if isValid then 
                            first.MergeNested()
                            second.MergeNested()
                            reraise()
                        else
                            raise (CommitFailedException())
                    | :? CommitFailedException 
                    | :? ThreadInterruptedException ->
                        reraise()
                    | _ ->
                        second.Lock()
                        let isValid = second.IsValid()
                        second.UnLock()
                        if isValid then
                            second.MergeNested()
                            reraise()
                        else
                            raise (CommitFailedException())
            | :? CommitFailedException
            | :? ThreadInterruptedException ->
                reraise()
            | _ ->
                first.Lock()
                let isValid = first.IsValid()
                first.UnLock()
                if isValid then
                    first.MergeNested()
                    reraise()
                else raise (CommitFailedException())

    member this.OrElse(p: TLog -> unit, q: TLog -> unit) = this.OrElse<_>(p, q) |> ignore

type Stm<'T> = (TLog -> 'T)

let newTVar (value : 'T) : TVar<'T> =
    TLog.NewTVar(value)
    
let readTVar (ref : TVar<'T>) : Stm<'T> =
    fun trans -> trans.ReadTVar(ref)
    
let writeTVar (ref : TVar<'T>) (value : 'T) : Stm<unit> =
    fun trans -> trans.WriteTVar(ref, value)

let retry () : Stm<'T> = 
    fun trans -> trans.Retry<_>()

let orElse (a : Stm<'T>) (b : Stm<'T>) : Stm<'T> = 
    fun trans -> trans.OrElse<_>((fun x -> a x), (fun x -> b x))
    
let atomically (a : Stm<'T>) : 'T =
    TLog.Atomic<_>(fun x -> a x)
    
type StmBuilder () =
    member _.Return(x) : Stm<_> = fun _ -> x

    member _.ReturnFrom(m) : Stm<_> = m

    member _.Bind(p : Stm<_>, rest : _ -> Stm<_>) : Stm<_> = fun trans -> rest (p trans) trans

    member _.Let(p, rest) : Stm<_> = rest p 
    
    member _.Delay(f : unit -> Stm<_>) : Stm<_> = fun trans -> f () trans
    
    member _.Combine(p, q) : Stm<_> = orElse p q

    member _.Zero() = retry ()
    
let stm = new StmBuilder ()

let ifM p x = if p then x else stm.Return(())

let liftM f x = stm { let! x' = x in return f x' }

let sequence (ms : seq<Stm<_> >) : Stm<seq<_> > =
    fun trans -> ms |> Seq.map (fun x -> x trans) |> Seq.cache

let mapM f ms = ms |> Seq.map f |> sequence

let sequence_ (ms : seq<Stm<_> >) : Stm<_> =
    fun trans -> ms |> Seq.iter (fun x -> x trans)

let mapM_ f ms = ms |> Seq.map f |> sequence_

let filterM p ms =
    let mark x = stm { let! v = p x in return  v, x }
    mapM mark ms |> liftM (Seq.filter fst >> Seq.map snd)