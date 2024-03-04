module Predictor.Test.Bar

open NUnit.Framework
open Assertion
open Predictor.Lib
open System.Collections.Generic
open Predictor.MimicPool
open Predictor.Model
open Mirage.Utilities.LVar
open FSharpPlus
open System
open Predictor.Utilities
open Mirage.Utilities.Print
open Predictor.Domain
open Predictor.Config
open System.Linq 
open Predictor.Learner

let printInfo (s : string) : unit = printfn "INFO: %s" s
let printWarning (s : string) : unit = printfn "WARNING: %s" s
let printError (s : string) : unit = printfn "ERROR: %s" s


type PlayerClass = Guid

type TextId =
    {   text: string
        id: Guid
    }

type SeqAction =
    | Wait of int
    | Speak of string * Guid * (Guid list)
    | SpeakUser of TextId * (Guid list)
    | Parallel of (SeqAction list) list
    | StartMimic of Guid * (Guid list)
    | EndMimic of Guid
    | SetNotAfk
    | SetIsAfk

[<TestFixture>]
type public Test() =

    let uid = Guid.Empty
    let defaultConfig = config
    let mkTextId (s: string) = { text = s; id = Guid.NewGuid() }
    let wordDurations = Array.init 30 (fun i -> 233 + 49*i)
    let isActiveLVar = newLVar true

    let defaultWhisperDelay = 300
    let defaultWordDelay = 200
    let defaultVadRepeat = 30
    let mutable whisperDelay = defaultWhisperDelay
    let mutable wordDelay = defaultWordDelay
    let mutable vadRepeat = defaultVadRepeat // vad has basically 0 delay

    let mutable startDate = DateTime.Now

    let getNowMillis () = timeSpanToMillis <| DateTime.Now - startDate


    let mimicPrintListLVar : LVar<List<int * string * string>> = newLVar(System.Collections.Generic.List())

    let getMimicPrintList = accessLVar mimicPrintListLVar <| fun mimicPrintList ->
        System.Collections.Generic.List(mimicPrintList)

    let computeWhisperTimings (tokens: string array) (speakTimings: List<int * int>) (t: int) = 
        let whisperTimings : List<int * string> = System.Collections.Generic.List()
        let mutable lastTime = t
        let mutable i = -1
        let mutable prefix = ""
        while lastTime < snd speakTimings[speakTimings.Count - 1] do
            let gatherEnd = lastTime + whisperDelay
            while i + 1 < speakTimings.Count && snd speakTimings[i + 1] <= gatherEnd do
                i <- i + 1
                prefix <- String.concat " " [|prefix; tokens[i]|]

            let whisperReportTime = gatherEnd + whisperDelay
            whisperTimings.Add((whisperReportTime, prefix))

            lastTime <- lastTime + whisperDelay
            ()
        whisperTimings

    let computeVADTimings (tokens: string array) (speakTimings: List<int * int>) (s: int) =
        let vadTimings : List<int> = System.Collections.Generic.List()
        let mutable lastTime = s + vadRepeat
        for (startTime, endTime) in speakTimings do
            while lastTime < startTime do
                lastTime <- lastTime + vadRepeat
            while lastTime <= endTime do
                vadTimings.Add(lastTime)
                lastTime <- lastTime + vadRepeat
            
            // There is a slight bug where the last segment of a word may not be included in the vad, but it probably doesn't matter in these tests; real gameplay is already way more messy than this

        vadTimings

    let computeSpeakTimings (tokens: string array) (t: int) =
        let speakTimings : List<int * int> = System.Collections.Generic.List()
        let mutable lastTime = t
        for word in tokens do
            let wordEndTime = lastTime + wordDurations[word.Length]
            speakTimings.Add((lastTime, wordEndTime))
            lastTime <- wordEndTime + wordDelay
        speakTimings

    let createClassesMap 
        (mimicIdToPlayerClass : Map<Guid, Guid>)
        (idToNames: Map<Guid, string>) : Map<Guid, Guid> =
        let mutable res = Map.empty
        for kv in idToNames do
            let id = kv.Key
            if mimicIdToPlayerClass.ContainsKey(id) then
                res <- res.Add(id, mimicIdToPlayerClass[id])
            else
                res <- res.Add(id, id)
        res

    // Blocks until the last utterance is said, but will still run a background thread that will do the whisper update for the last utterance.
    let processTalk 
        (listenerIds: Guid list) 
        (speakerId: Guid) 
        (text: string) 
        (t: int) 
        (guidOption: Guid option) 
        (classes: Map<Guid, PlayerClass>) 
        (mimics: Set<Guid>)
        (idToNames: Map<Guid, string>)
        (isMimic: bool) : Async<int> = 
        async {
            let tokens = text.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
            let speakTimings = computeSpeakTimings tokens t
            let whisperTimings = computeWhisperTimings tokens speakTimings t
            let vadTimings = computeVADTimings tokens speakTimings t
            let newTime = snd speakTimings[speakTimings.Count - 1]
            let utteranceTime = newTime - t

            logInfo <| $"[{t}-{snd speakTimings[speakTimings.Count - 1]}]" + (if isMimic then " (mimic) " else " ") + $"{idToNames[speakerId]}: {text}"
            let startTime = DateTime.Now

            let emitWhisper =
                async {
                    let mutable lastTime = t
                    for whisperTime, whisperText in whisperTimings do
                        do! Async.Sleep (whisperTime - lastTime)
                        lastTime <- whisperTime

                        // logInfo $"{whisperTime}"
                        let spokeAtom =
                            SpokeAtom {
                                text = whisperText
                                audioOption = None
                                start = startTime
                            }

                        if speakerId = uid then
                            userRegisterText spokeAtom
                        elif mimics.Contains(speakerId) then
                            mimicRegisterText speakerId spokeAtom

                        let heardAtom =
                            HeardAtom {
                                text = whisperText
                                speakerClass = EntityId.Guid classes[speakerId]
                                speakerId = EntityId.Guid speakerId
                                start = startTime
                            }
                        for listenerId in listenerIds do
                            if listenerId = uid then
                                userRegisterText heardAtom
                            elif mimics.Contains(listenerId) then
                                mimicRegisterText listenerId heardAtom

                    // one last emit to register the audio file if necessary
                    if speakerId = uid && guidOption.IsSome then
                        let audioOption = Some <| {   
                                fileId = guidOption.Value
                                duration = utteranceTime
                            }
                        userRegisterText <| SpokeAtom {
                            text = text // this text is from the outer scope
                            audioOption = audioOption
                            start = startTime
                        }
                }

            let emitVad =
                async {
                    let mutable lastTime = t
                    for vadTime in vadTimings do
                        do! Async.Sleep (vadTime - lastTime)
                        lastTime <- vadTime

                        let time = startDate.AddMilliseconds vadTime
                        let everyone = List.append listenerIds [speakerId]
                        let voiceActivityAtom = 
                            VoiceActivityAtom {
                                speakerId = EntityId.Guid speakerId
                                time = time
                            }
                        for id in everyone do
                            if id = uid then
                                userRegisterText voiceActivityAtom
                            elif mimics.Contains(id) then
                                mimicRegisterText id voiceActivityAtom
                }

            Async.Start emitWhisper
            Async.Start emitVad
            do! Async.Sleep utteranceTime
            return newTime
        }

    // Send to accumulator and also send the data back into the simulation
    let handleMimicPrint 
        (mimicId: Guid) 
        (listenerIds: Guid list) 
        (name: string) 
        (fileIdToText: Map<Guid, string>) 
        (classes: Map<Guid, PlayerClass>) 
        (mimics: Set<Guid>)
        (idToNames: Map<Guid, string>)
        (fileId: Guid) 
        = 
        if fileIdToText.ContainsKey(fileId) then
            let now = getNowMillis()
            let text = fileIdToText[fileId]
            Async.Start <| async {
                let! _ = processTalk listenerIds mimicId text (getNowMillis()) None classes mimics idToNames true
                ()
            }

            Async.Start <| async {
                let! _ = accessLVar mimicPrintListLVar <| fun mimicPrintList ->
                    mimicPrintList.Add((now, name, text))
                ()
            }
        else
            logInfo <| sprintf "Key not found: %A" fileId

    let mimicPrintNothing (_: Guid) : unit = ()


    let rec processActions 
        (actions: SeqAction list) 
        (t: int) 
        (classes: Map<Guid, PlayerClass>) 
        (mimics: Set<Guid>)
        (idToNames: Map<Guid, string>)
        (fileIdToText: Map<Guid, string>): Async<int> = 
        async {
            match actions with
            | [] -> return t
            | x :: xs -> 
                match x with
                | Wait duration ->
                    do! Async.Sleep duration
                    return! processActions xs (t + duration) classes mimics idToNames fileIdToText
                | Speak (text, speakerId, listenerIds) ->
                    let! newTime = processTalk listenerIds speakerId text t None classes mimics idToNames false
                    return! processActions xs newTime classes mimics idToNames fileIdToText
                | SpeakUser (textId, listenerIds) ->
                    let! newTime = processTalk listenerIds uid textId.text t (Some textId.id) classes mimics idToNames false
                    return! processActions xs newTime classes mimics idToNames fileIdToText
                | Parallel subActions ->
                    let! finishTimes = Async.Parallel <| map (fun actions -> processActions actions t classes mimics idToNames fileIdToText) subActions
                    return! processActions xs (Array.max finishTimes) classes mimics idToNames fileIdToText
                | StartMimic (id, listenerIds) ->
                    mimicInit id <| handleMimicPrint id listenerIds (idToNames[id]) fileIdToText classes mimics idToNames
                    printInfo <| sprintf "[%d] Started mimic: %s" t idToNames[id]
                    return! processActions xs t classes mimics idToNames fileIdToText
                | EndMimic id ->
                    mimicKill id
                    printInfo <| sprintf "[%d] Killed mimic: %s" t idToNames[id]
                    return! processActions xs t classes mimics idToNames fileIdToText
                | SetIsAfk -> 
                    printInfo <| sprintf "[%d] toggled learning OFF." t
                    setUserIsInactive()
                    let! _ = writeLVar isActiveLVar false
                    return! processActions xs t classes mimics idToNames fileIdToText
                | SetNotAfk -> 
                    printInfo <| sprintf "[%d] toggled learning ON." t
                    userIsActivePing()
                    let! _ = writeLVar isActiveLVar true
                    return! processActions xs t classes mimics idToNames fileIdToText

        }

    // Note to self: if this is broken, remember to have moved over the _internal and the model to the test directory's build folder.
    // [<OneTimeSetUp>] seems to not work when I run individual tests with --filter Test.Foo, so instead we try doing this with [<SetUp>]
    let mutable hasBeenInit = false

    [<SetUp>]
    member this.init () = 
        if not hasBeenInit then
            hasBeenInit <- true
            logInfo $"[{getNowMillis()}] Init Test"
            Async.Start << repeatAsync 500 <| async {
                let! active = readLVar isActiveLVar
                if active then
                    userIsActivePing()
            }
            Async.RunSynchronously <| initBehaviourPredictor printInfo printWarning printError uid "E:/temp/data" 2000000

    member this.reset () =
        startDate <- DateTime.Now
        config <- defaultConfig
        whisperDelay <- defaultWhisperDelay
        wordDelay <- defaultWordDelay
        vadRepeat <- defaultVadRepeat
        Async.RunSynchronously <| async {
            let! _ = writeLVar isActiveLVar true
            let! _ = accessLVar mimicPrintListLVar <| fun mimicPrintList ->
                mimicPrintList.Clear()
            do! clearMemory
        }

    [<Test>]
    // Test that the gameinputstatistics queues are cleared with enough time
    member this.clearStatisticsAfterNotTalking () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000 }
            let bob = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList []
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob")]
            let fakerWhatWasThat = mkTextId "faker what was that"
            let replyTexts = [fakerWhatWasThat]
            let actions = 
                [   
                    Wait 1000;
                    Speak ("happy feet, wombo combo", bob, [uid]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    Wait 10000;
                ]


            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            do! printModel
            let! result = accessLVar modelLVar <| fun model ->
                let policy = model.policy
                let first = policy.First()
                logInfo <| sprintf "FIRST %A" first
                let obs, _ = first.Value
                let isNone (o: ObsEmbedding) =
                    match o with
                    | Prev -> false
                    | Value (Some _) -> false
                    | Value None -> true

                isNone obs.heardEmbedding && isNone obs.spokeEmbedding
            return result
        }
        assertTrue result "Did not succssfully clear"

    [<Test>]
    member this.clearStatisticsFromLongParagraph () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000 }
            let bob = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList []
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob")]
            let fakerWhatWasThat = mkTextId "faker what was that"
            let replyTexts = [fakerWhatWasThat]
            let actions = 
                [   
                    Wait 1000;
                    Speak ("hello 1.", bob, [uid]);
                    Speak ("hello 2.", bob, [uid]);
                    Speak ("hello 3.", bob, [uid]);
                    Speak ("hello 4.", bob, [uid]);
                    Speak ("hello 5.", bob, [uid]);
                    Speak ("hello 6.", bob, [uid]);
                    Speak ("hello 7.", bob, [uid]);
                    Speak ("hello 8.", bob, [uid]);
                    Speak ("hello 9.", bob, [uid]);
                    Speak ("hello 10.", bob, [uid]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                    SpeakUser (fakerWhatWasThat, [bob]);
                ]


            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            do! printModel
            return false
        }
        assertTrue result "TODO automate testing. Print model and do an eyeball check."

    [<Test>]
    // In this series of tests, mallory should say "reply" to bob's "hello".
    // Test 1 (noPrompt): mallory does not reply when not prompted
    // Test 2 (hello): mallory replies to "hello"
    // Test 3 (notHello): mallory does not reply to "machine"
    member this.replyToHello_noPrompt () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000 }
            let bob = Guid.NewGuid()
            let mallory = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList [(mallory, uid)]
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob"); (mallory, "mallory")]
            let reply = mkTextId "reply"
            let replyTexts = [reply]
            let actions = [
                Wait 1000;
                Speak ("machine", bob, [uid])
                Wait 6000;
                Speak ("hello", bob, [uid])
                Wait 1000;
                SpeakUser (reply, [bob])
                Wait 6000;
                Speak ("bye", bob, [uid])
                Wait 1000;
                StartMimic (mallory, [])
                Wait 2000;
                EndMimic mallory
                Wait 100;
            ]

            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            let! mimicTimings = getMimicPrintList
            return mimicTimings.Count = 0
        }
        assertTrue result ""

    [<Test>]
    member this.replyToHello_hello () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000; SCORE_TALK_BIAS = 0.1 }
            whisperDelay <- 500
            let bob = Guid.NewGuid()
            let mallory = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList [(mallory, uid)]
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob"); (mallory, "mallory")]
            let reply = mkTextId "reply"
            let replyTexts = [reply]
            let actions = [
                Wait 1000;
                Speak ("machine", bob, [uid])
                Wait 6000;
                Speak ("hello", bob, [uid])
                Wait 3000;
                SpeakUser (reply, [bob])
                Wait 6000;
                Speak ("machine", bob, [uid])
                Wait 7000;
                StartMimic (mallory, [])
                Wait 1000;
                Speak ("hello", bob, [mallory])
                Wait 6000;
                EndMimic mallory
                Wait 100;
            ]

            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            let! mimicTimings = getMimicPrintList
            printfn "Mimic timings: %A" mimicTimings
            return mimicTimings.Count = 1
        }
        assertTrue result ""

    [<Test>]
    member this.replyToHello_notHello () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000 }
            let bob = Guid.NewGuid()
            let mallory = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList [(mallory, uid)]
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob"); (mallory, "mallory")]
            let reply = mkTextId "reply"
            let replyTexts = [reply]
            let actions = [
                Wait 1000;
                Speak ("machine", bob, [uid])
                Wait 6000;
                Speak ("hello", bob, [uid])
                Wait 1000;
                SpeakUser (reply, [bob])
                Wait 6000;
                Speak ("machine", bob, [uid])
                Wait 7000;
                StartMimic (mallory, [])
                Wait 1000;
                Speak ("machine", bob, [mallory])
                Wait 3000;
                EndMimic mallory
                Wait 100;
            ]

            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            let! mimicTimings = getMimicPrintList
            return mimicTimings.Count = 0
        }
        assertTrue result ""

    [<Test>]
    // Wait for X seconds, then say "apple" "pie" in two different recordings.
    // Now run a mimic for a large amount of seconds. The mimic should say "apple pie" in the same relative frequency.
    member this.testMonologue () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000; SCORE_TALK_BIAS = 0.0 }
            whisperDelay <- 100

            let mallory = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList [(mallory, uid)]
            let idToNames = Map.ofList [(uid, "user"); (mallory, "mallory")]
            let apple = mkTextId "apple"
            let pie = mkTextId "pie"
            let replyTexts = [apple; pie]
            let actions = [
                Wait 1000;
                SpeakUser (apple, []);
                Wait 2000;
                SpeakUser (pie, []);
                Wait config.VOICE_BUFFER;
                SetIsAfk;
                StartMimic (mallory, [])
                Wait 200000;
                EndMimic mallory;
                Wait 100;
            ]

            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            let! mimicTimings = getMimicPrintList
            printSeq mimicTimings
            printfn "Size: %d" mimicTimings.Count
            let mutable hasPie = false
            let mutable applePrecedesPie = true
            let mutable appleCount = 0
            let mutable pieCount = 0
            let mutable applePrev = 0
            for i in 1..(mimicTimings.Count - 1) do
                let (_, _, text) = mimicTimings[i]
                let (_, _, prevText) = mimicTimings[i-1]
                if text = "pie" then
                    pieCount <- pieCount + 1
                    hasPie <- true
                    if prevText = "apple" then
                        applePrev <- applePrev + 1
                else
                    appleCount <- appleCount + 1

            printfn $"{hasPie} {applePrecedesPie} {appleCount} {pieCount} {applePrev}"
            return abs (pieCount - applePrev) < 6 && hasPie && applePrecedesPie && abs(appleCount - pieCount) < 4 && 32 <= mimicTimings.Count && mimicTimings.Count <= 50
        }
        assertTrue result "" 

    [<Test>]
    // Make two mimics that should both reply to "hello"
    member this.testTwoMimics () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000; SCORE_TALK_BIAS = 0.1 }
            let bob = Guid.NewGuid()
            let mallory = Guid.NewGuid()
            let darth = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList [(mallory, uid); (darth, uid)]
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob"); (mallory, "mallory"); (darth, "darth")]
            let reply = mkTextId "reply"
            let replyTexts = [reply]
            let actions = [
                Wait 1000;
                Speak ("machine", bob, [uid])
                Wait 6000;
                Speak ("hello", bob, [uid])
                Wait 1200;
                SpeakUser (reply, [bob])
                Wait 6000;
                Speak ("machine", bob, [uid])
                Wait 7000;
                StartMimic (mallory, [])
                StartMimic (darth, [])
                Wait 1000;
                Speak ("hello", bob, [mallory; darth])
                Wait 3000;
                EndMimic mallory
                EndMimic darth
                Wait 100;
            ]

            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            let! mimicTimings = getMimicPrintList
            printfn "mimicTimings: %A" mimicTimings
            return mimicTimings.Count = 2
        }
        assertTrue result ""

    [<Test>]
    // Two different replies to "hello". Should be chosen with equiprobability.
    member this.twoReplies () =
        let result = Async.RunSynchronously <| async {
            this.reset()
            // These are filled in
            config <- { config with VOICE_BUFFER = 5000; SCORE_TALK_BIAS = 0.0 }
            let bob = Guid.NewGuid()
            let mallory = Guid.NewGuid()
            let darth = Guid.NewGuid()
            let mark = Guid.NewGuid()
            let mimicIdToPlayerClass = Map.ofList [(mallory, uid); (darth, uid); (mark, uid);]
            let idToNames = Map.ofList [(uid, "user"); (bob, "bob"); (mallory, "mallory"); (darth, "darth"); (mark, "mark")]
            let reply1 = mkTextId "reply 1"
            let reply2 = mkTextId "reply 2"
            let warmup = mkTextId "warmup"
            let replyTexts = [warmup; reply1; reply2]
            let actions = [
                Wait 1000;
                Speak ("start", bob, [uid])
                Wait 1200;
                SpeakUser (warmup, [])
                Wait 6000;
                Speak ("hello", bob, [uid])
                Wait 1200;
                SpeakUser (reply1, [bob])
                Wait 6000;
                Speak ("hello", bob, [uid])
                Wait 1200;
                SpeakUser (reply2, [bob])
                Wait 6000;
                SetIsAfk;
                StartMimic (mallory, [])
                StartMimic (darth, [])
                StartMimic (mark, [])
                Wait 1000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                Speak ("hello", bob, [mallory; darth; mark])
                Wait 10000;
                EndMimic mallory
                EndMimic darth
                EndMimic mark
                Wait 100;
            ]

            // Do not touch these
            let fileIdToText = Map.ofList <| map (fun x -> (x.id, x.text)) replyTexts
            let classesMap = createClassesMap mimicIdToPlayerClass idToNames
            let mimicsSet = Set.ofList <| List.ofSeq mimicIdToPlayerClass.Keys
            let! _ = processActions actions 0 classesMap mimicsSet idToNames fileIdToText
            let! mimicTimings = getMimicPrintList
            printSeq mimicTimings
            let mutable reply1Count = 0
            let mutable reply2Count = 0
            let mutable warmupCount = 0
            for i in 0..(mimicTimings.Count - 1) do
                let (_, _, text) = mimicTimings[i]
                if text = "reply 1" then
                    reply1Count <- reply1Count + 1
                elif text = "reply 2" then
                    reply2Count <- reply2Count + 1
                else
                    warmupCount <- warmupCount + 1

            let ratio = float reply1Count / float reply2Count
            printfn "Balance: %d %d %f %d" reply1Count reply2Count ratio warmupCount
            return 0.6 <= ratio && ratio <= 1.4 && warmupCount <= reply1Count / 2
        }
        assertTrue result ""

    [<Test>]
    member this.healthTest () =
        // Create a bunch of mimics and kill them, check that global locks have been released
        for _ in 1..5 do
            let random = Random()
            Async.RunSynchronously <| async {
                let life (id : Guid) = 
                    async {
                        mimicInit id mimicPrintNothing
                        let sleepDuration: int = int <| 2000.0 * random.NextDouble()
                        do! Async.Sleep sleepDuration
                        mimicKill id
                        do! Async.Sleep 1000
                    }
                let jobs = Array.init 50 (fun _ -> life <| Guid.NewGuid())
                let! _ = Async.Parallel jobs

                let! _ = accessLVar mimicsLVar <| fun _ -> ()
                let! _ = accessLVar modelLVar <| fun _ -> ()
                ()
            }
            ()
        assertTrue true ""

    [<Test>]
    member this.testInstantMimicKill () =
        Async.RunSynchronously <| async {
            let id = Guid.NewGuid()
            let! beforeSize = accessLVar mimicsLVar <| fun mimics -> mimics.Count
            mimicInit id mimicPrintNothing
            mimicKill id
            do! Async.Sleep 1000
            let! afterSize = accessLVar mimicsLVar <| fun mimics -> mimics.Count
            assertTrue (beforeSize = 0 && beforeSize = afterSize) $"Sizes are not the same {beforeSize} {afterSize}"
        }

