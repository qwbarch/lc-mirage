module Predictor.ActionSelector
open Mirage.Utilities.LVar
open Domain
open System
open System.Collections.Concurrent
open Utilities
open Config
open Mirage.Utilities.Print
open System.Collections.Generic
open Embedding
open FSharpPlus
open System.Linq

let NOSPEECH_NOSPEECH = 2.0
let SPEECH_NOSPEECH = 0.0

let SIM_OFFSET = -0.2
let SIM_SCALE = 10.0

let TIME_AREA = 5.0
let TIME_SIGNAL = 3.0
let SCORE_SPEAK_FACTOR = 0.3

let simTransform (x: float) = SIM_SCALE * (x + SIM_OFFSET)

let speakOrHearDiffToCost (policyDiff: int) (curDiff: int) (rngSource: RandomSource) =
    if policyDiff > 10000000 || curDiff > 10000000 then
        0.0
    else
        let l : int = min policyDiff <| max 300 (policyDiff / 2)
        let r = max (l + (int <| float config.MIL_PER_OBS * TIME_AREA)) <| policyDiff + policyDiff / 2
        let split = max 1 <| (r - l) / config.MIL_PER_OBS
        let zzz = clamp 0.0 1.0 <| TIME_AREA / float split
        // printfn "%d %d %d %d %d %f" policyDiff curDiff l r split zzz
        if l <= curDiff && curDiff <= r then
            let prob = clamp 0.0 1.0 <| TIME_AREA / float split
            // printfn "%d %d %d %d %d %f" policyDiff curDiff l r split prob
            if rngSource.NextDouble() < prob then
                TIME_SIGNAL
            else
                0.0
        else
            0.0

let computeScores 
    (heardSims: List<float>)
    (spokeSims: List<float>)
    (policy: KeyValuePair<DateTime, CompressedObservation * FutureAction> seq)
    (observation: Observation)
    (rngSource: RandomSource) : List<float * FutureAction> =
    let flattened = 
        let temp = List<float * float * DateTime * CompressedObservation * FutureAction>()
        let mutable i = 0
        // printfn "----------------------------------------------"
        // printfn "obs %A" observation
        for kv in policy do
            let policyObs, action = fst kv.Value, snd kv.Value
            temp.Add((heardSims[i], spokeSims[i], kv.Key, policyObs, action))
            i <- i + 1
        temp

    flip map flattened <| fun (heardSim, spokeSim, _, policyObs, action) ->
        let talkBias =
            match action with
            | NoAction -> 0.0
            | QueueAction _ -> config.SCORE_TALK_BIAS

        let speakTimeCost = SCORE_SPEAK_FACTOR * speakOrHearDiffToCost policyObs.lastSpoke observation.lastSpoke rngSource
        let hearTimeCost = speakOrHearDiffToCost policyObs.lastHeard observation.lastHeard rngSource
        let speakOrHearTimeCost = max speakTimeCost hearTimeCost
        let totalCost = heardSim + spokeSim + talkBias + speakOrHearTimeCost
        // match action with
        // | NoAction -> ()
        // | QueueAction _ -> printfn "action %f %f %f %f %f %O %O" totalCost spokeSim heardSim speakTimeCost hearTimeCost policyObs action

        totalCost, action

let sample (unnormScores: List<float * FutureAction>) (rngSource: RandomSource) =
    // TODO use a heap instead of sorting
    let scoresMean = (map (fun (s, _) -> s) unnormScores |> sum) / float unnormScores.Count
    let scoresOrd : List<float * int> = map (fun i -> (fst unnormScores[i] - scoresMean, i)) <| List(Array.init unnormScores.Count (fun i -> i))

    scoresOrd.Sort()
    scoresOrd.Reverse()

    let mx = fst scoresOrd[0]
    let cand = List()
    for score, i in scoresOrd do
        if (abs (mx - score) < 0.01) then
            cand.Add((score, i))

    let scores : float array =
        let scoresList: List<float> = map fst scoresOrd
        scoresList.ToArray()

    let distribution = softmax scores
    let scoresOrdChoiceInd = weightedSample distribution
    let choice = snd scoresOrd[scoresOrdChoiceInd]
    unnormScores[choice]


let sampleAction (oppositeOrdPolicy: Policy) (observation: Observation) (rngSource: RandomSource) : FutureAction = 
    if oppositeOrdPolicy.Count = 0 then
        NoAction
    else
        let policy = Seq.rev oppositeOrdPolicy

        let computeSims
            (policyObsEmbs: seq<ObsEmbedding>)
            (target: Option<string * TextEmbedding>) =
            let sims : List<float> = List()
            let mutable lastSim = SPEECH_NOSPEECH
            for obsEmb in policyObsEmbs do
                lastSim <-
                    match obsEmb with
                    | Prev -> lastSim
                    | Value None -> 
                        match target with
                        | None -> NOSPEECH_NOSPEECH
                        | Some (_, _) -> SPEECH_NOSPEECH
                    | Value (Some (_, textEmbedding)) -> 
                        match target with
                        | None -> SPEECH_NOSPEECH
                        | Some (_, observationEmbedding) -> simTransform <| embeddingSim textEmbedding observationEmbedding
                sims.Add(lastSim)
            sims

        let policyHeardEmbs, policySpokeEmbs =
            let heard = List<ObsEmbedding>()
            let spoke = List<ObsEmbedding>()
            for kv in policy do
                let comp = fst kv.Value
                heard.Add(comp.heardEmbedding)
                spoke.Add(comp.spokeEmbedding)
            heard, spoke

        let heardSims = computeSims policyHeardEmbs observation.heardEmbedding
        let spokeSims = computeSims policySpokeEmbs observation.spokeEmbedding
        let scores = computeScores heardSims spokeSims policy observation rngSource
        let sampled = sample scores rngSource
        // printfn "SAMPLED %A" sampled
        snd sampled
let observationToFutureAction (internalPolicy: LVar<Policy>) (observation : Observation) (rngSource: RandomSource) : Async<Option<FutureAction>> =
    async {
        let! res = accessLVar internalPolicy <| fun policy ->
            if policy.Count = 0 then
                None
            else
                Some <| sampleAction policy observation rngSource
        return res
    }

let createFutureActionGeneratorAsync
    (internalPolicy: LVar<Policy>)
    (observationChannel: LVar<DateTime -> Observation>)
    (sendToActionEmitter: FutureAction -> unit)
    (rngSource: RandomSource) =
    repeatAsync config.MIL_PER_OBS <| async {
        let timeStart = DateTime.Now
        let! observationProducer = readLVar observationChannel
        let observation = observationProducer timeStart
        let! futureActionOption = observationToFutureAction internalPolicy observation rngSource
        if futureActionOption.IsSome then
            sendToActionEmitter futureActionOption.Value
        ()
    }