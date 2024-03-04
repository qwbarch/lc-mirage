module App

open Predictor.Lib
open Predictor.Domain
open Embedding
open System.Collections.Generic
open System
open System.Threading
open System.Collections.Concurrent
open Mirage.Utilities.MVar
open Mirage.Utilities.Lock
open Mirage.Utilities.Print
open Mirage.Utilities.AtomicFile
open System.Diagnostics
open FSharpPlus
open Predictor.MimicPool
open Microsoft.FSharp.Control
open System.Threading.Tasks
open Mirage.Utilities.Async
open System.Linq
open Mirage.Utilities.LVar
open Predictor.Model
open Predictor.Utilities
open Predictor.FileHandler
open FSharp.Json
open MathNet.Numerics.Distributions
open MathNet.Numerics.Random

let preconcat (text : string) =
    let tokens = text.Split([|' '|], StringSplitOptions.RemoveEmptyEntries) 
    let cums = List<string>([tokens[0]])
    for i in 1..(tokens.Length - 1) do
        let toAdd = String.concat " " [cums[i-1]; tokens[i]]
        cums.Add(toAdd)
    cums

let mimicRegisterTextDefault 
    (mimicId: Guid) 
    (speakerId: Guid) 
    (speakerClass: Guid) 
    (text: string)
    (spokeOrHeard: EitherSpokeHeard)
    =
    let now = DateTime.Now
    let tokens = text.Split([|' '|], StringSplitOptions.RemoveEmptyEntries) 
    let start = now.Subtract(TimeSpan.FromMilliseconds(float <| 1000 * tokens.Length))
    let duration = timeSpanToMillis (now - start)
    let gameInput =
        match spokeOrHeard with
        | Spoke ->
            SpokeAtom {
                text = text
                audioOption = None
                start = start
            }
        | Heard ->
            HeardAtom {
                text = text
                speakerClass = EntityId.Guid speakerClass
                speakerId = EntityId.Guid speakerId
                start = start
            }

    mimicRegisterText mimicId gameInput

let userRegisterTextDefault
    (speakerId: Guid)
    (speakerClass: Guid)
    (text: string)
    (audioOption: AudioInfo option)
    (spokeOrHeard: EitherSpokeHeard)
    =
    match spokeOrHeard with
    | Spoke -> userIsActivePing()
    | Heard -> ()

    let now = DateTime.Now
    let tokens = text.Split([|' '|], StringSplitOptions.RemoveEmptyEntries) 
    let start = now.Subtract(TimeSpan.FromMilliseconds(float <| 1000 * tokens.Length))
    let gameInput =
        match spokeOrHeard with
        | Spoke ->
            SpokeAtom {
                text = text
                audioOption = audioOption
                start = start
            }
        | Heard ->
            HeardAtom {
                text = text
                speakerClass = EntityId.Guid speakerClass
                speakerId = EntityId.Guid speakerId
                start = start
            }

    userRegisterText gameInput

let printInfo (s : string) : unit = printfn "INFO: %s" s
let printWarning (s : string) : unit = printfn "WARNING: %s" s
let printError (s : string) : unit = printfn "ERROR: %s" s

let id = Guid.NewGuid()

let receiveMimicResponse (g : Guid) : unit = printfn "Mimic sends %A" g
let t1 = async {
    mimicInit id receiveMimicResponse
    do! Async.Sleep 20
    let _ = mimicRegisterTextDefault id id id "hello world"
    ()
}

let t2 = async {
    do! Async.Sleep 1000
    userRegisterTextDefault id id "hello user" None Heard
}


let testBackwardsIterate () = 
    let sw = Stopwatch()
    sw.Start()
    let dict = SortedDictionary<int, int>()
    for i in 1..1000000 do
        dict.Add(i, i+1)

    let predicate (kv: KeyValuePair<int,int>) = kv.Key >= 57
    let predicate2 (kv: KeyValuePair<int,int>) = kv.Key <= 57

    let mutable tot = 0
    for i in 1..1000 do
        // let backwardsIterator = System.Linq.Enumerable.Reverse(dict)
        // tot <- tot + Enumerable.First(dict, predicate).Key
        // printfn "%A" tot
        let res = Enumerable.TakeWhile(dict, predicate2)
        if i = 1 then
            printSeq res
        ()
    printfn $"{sw.ElapsedMilliseconds}"
    printfn "%A" tot
    ()


let rngSuccess : Async<bool> = async {
    let random = Random()
    if random.NextDouble() > 0.5 then
        printfn "here"
        invalidOp ""
    return random.NextDouble() < 0.3
}

let spamEncode (text: string) (truth: (string * float32 array) option) (endTime: DateTime) = 
    async {
        while DateTime.Now < endTime do
            let! res = encodeText "hello"
            printf "done"
            ()
    }

let spamPrintStdout (endTime: DateTime) = 
    async {
        while DateTime.Now < endTime do
            printf $"a"
            ()
    }


[<EntryPoint>]
let main _ =
    let sw = Stopwatch()
    sw.Start()
    let g = Gamma(2.0, 1.5)
    let mutable t = 0.0
    let r = Mcg31m1()
    for i in 1..10000000 do
        // let x = g.Sample()
        let x = r.NextDouble()
        t <- t + x
    // let x = Random.doubles 1000000
    // let t = Array.sum x
    printfn "%d" sw.ElapsedMilliseconds
    printfn "%f" t


    // let z: SortedDictionary<int, SortedDictionary<int, int>> = SortedDictionary()
    // z[2][3] <- 4
    // printfn "%A" z
    // Async.RunSynchronously <| async {
    //     let! truth = encodeText "hello"

    //     let endTime = DateTime.Now.AddMilliseconds(5000)
    //     let! _ = Async.Parallel [spamEncode "hello" truth endTime; spamPrintStdout endTime]
    //     do! Async.Sleep 1000

    //     let! check = encodeText "hello"
    //     printfn "%A" check.Value
    // }
    // Async.RunSynchronously <| async {
    //     let! res = encodeText "hello"
    //     printfn "%A" res
    //     // do! exponentialRepeat 200 20 rngSuccess
    // }
    // testBackwardsIterate()
    // Async.RunSynchronously <| atomicFileWrite "E:/temp/data/foo" "hello" true logInfo logError
    // Async.RunSynchronously <| removeTempFiles "E:/temp/data" logInfo logError
    // // ignore <| Async.RunSynchronously (encodeText "hello")


    // Async.RunSynchronously <| async {
    //     do! initBehaviourPredictor printInfo printWarning printError "e:/temp/data" 2000000
    //     userIsActivePing()
    //     logInfo "Start"
    //     // let! _ = Async.Parallel [t2]
    //     do! Async.Sleep 1500
    //     mimicInit id receiveMimicResponse
    //     userRegisterTextDefault (Guid.NewGuid()) "Hello" (Some { fileId = Guid.Empty ; duration = 10000 }) Heard
    //     do! Async.Sleep 2000

    //     userRegisterTextDefault id "got" (Some { fileId = Guid.Empty ; duration = 10000 }) Spoke
    //     // // mimicKill id
    //     // do! Async.Sleep 7000

    //     // do! printModel

    //     do! Async.Sleep 1000
    // }
    0