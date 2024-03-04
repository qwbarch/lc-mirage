module Predictor.Utilities
open System
open Domain
open Config
open System.Collections.Generic
open FSharpPlus

let mutable logInfo = fun (s: string) -> printfn "INFO: %s" s
let mutable logWarning = fun (s: string) -> printfn "WARNING: %s" s
let mutable logError = fun (s: string) -> printfn "ERROR: %s" s

// This exists since TimeSpan::Milliseconds returns the component, rather than a total value, and
// TimeSpan::TotalMilliseconds returns a float which often requires a conversion first.
// Note: this cuts down the range of possible lengths to approx 22 days.
let timeSpanToMillis (t : TimeSpan) = int << min 2000000000.0 <| floor t.TotalMilliseconds

let repeatAsync (repeatMillis: int) (f: Async<'T>) =
    let rec loop () = 
        async {
            let timeStart = DateTime.Now
            let targetWakeup = timeStart.AddMilliseconds(repeatMillis)

            let! _ = f

            let now = DateTime.Now
            let waitMillis : int = max 0 <| timeSpanToMillis (targetWakeup - now)
            do! Async.Sleep waitMillis
            do! loop()
        }
    loop()

let rec exponentialRepeat (repeatMillis: int) (maxTries: int) (job : Async<bool>) =
    async {
        let onFail = 
            async {
                if maxTries > 0 then
                    do! Async.Sleep repeatMillis
                    do! exponentialRepeat (repeatMillis * 2) (maxTries - 1) job
            }
        try
            let! res = job
            if not res then
                do! onFail
        with
        | _ -> do! onFail
    }

let textEmbeddingEq (a : TextEmbedding) (b : TextEmbedding) =
    let mutable eq = true
    for i in 0..(a.Length-1) do
        if abs(a[i] - b[i]) > 1e-7f then
            eq <- false
    eq

let optionStringTextEmbeddingEq (a: Option<String * TextEmbedding>) (b: Option<String * TextEmbedding>) =
    if a.IsNone && b.IsNone then
        true
    elif a.IsSome && b.IsSome then
        if fst a.Value = fst b.Value then
            textEmbeddingEq (snd a.Value) (snd b.Value)
        else
            false
    else
        false

let toObsEmbedding (prevValueOption: Option<String * TextEmbedding> option) (newValue: Option<String * TextEmbedding>)  =
    if newValue.IsNone || prevValueOption.IsNone then
        Value newValue
    elif optionStringTextEmbeddingEq prevValueOption.Value newValue then
        Prev
    else
        Value newValue

let replaceDict (dict: SortedDictionary<'T, 'V>) (key: 'T) (newValue: 'V) =
    if dict.ContainsKey key then
        let _ = dict.Remove(key)
        ()

    dict.Add(key, newValue)

let deepCopyNestedDict
    (queue: SortedDictionary<'T, SortedDictionary<'U, 'V>>) =
    let res = SortedDictionary()
    for kv in queue do
        res.Add(kv.Key, SortedDictionary(kv.Value))
    res


let sortedDictToMap (dict: SortedDictionary<'U, 'V>) : Map<'U, 'V> =
    let kvs = List()
    for kv in dict do
        kvs.Add(kv.Key, kv.Value)
    Map.ofSeq kvs

let clamp (low: 'T) (high: 'T) (x: 'T) = min high <| max low x

let softmax (scores: float array) : float array =
    let raised : float array = map (fun s -> Math.Exp(s)) scores
    let tot = sum raised
    map (fun s -> s / tot) raised

let weightedSample (weights: float array) : int = 
    let dist = MathNet.Numerics.Distributions.Categorical(weights)
    dist.Sample()