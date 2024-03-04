module Predictor.Model
open Domain
open System.Collections.Generic
open Mirage.Utilities.LVar
open Utilities
open System
open FileHandler

let mutable userId = Guid.Empty

// Sort by decreasing time since iteration forwards quickly is possible but idk how to iterate backwards.
type ModelPolicyComparator() =
    interface IComparer<DateTime> with
        member this.Compare(x, y) =
            compare y x

let emptyModel () : Model = {
    policy = SortedDictionary(ModelPolicyComparator())
    lastSpokeEncoding = None
    lastHeardEncoding = None
}

let modelLVar = newLVar(emptyModel())

let copyModelPolicy : Async<Policy> =
    async {
        let! contentsCopy = accessLVar modelLVar <| fun model ->
            let contents = List<KeyValuePair<DateTime, CompressedObservation * FutureAction>>()
            for kv in model.policy do
                contents.Add(kv)
            contents
        
        let policy = SortedDictionary<DateTime, CompressedObservation * FutureAction>(ModelPolicyComparator())
        for (kv: KeyValuePair<DateTime, CompressedObservation * FutureAction>) in contentsCopy do
            policy.Add(kv.Key, kv.Value)
        return policy
    }

let loadModel (fileState: FileState) = async {
    logInfo "Called loadModel"
    let! _ = accessLVar modelLVar <| fun model ->
        for kv in fileState.fileToData do
            let data = kv.Value
            let mutable failCount = 0
            for (obsFileFormat, action) in data do
                let obs = fromCompressedObsFileFormat obsFileFormat
                let time = obs.time
                let succ = model.policy.TryAdd(time, (obs, action))
                if not succ then
                    failCount <- 1
                ()

            if failCount > 0 then
                logWarning $"Found duplicate date entries: {failCount}"
                // TODO initialize lastSpokeEncoding, lastHeardEncoding, though I don't think this really matters outside of a small performance bump on startup
    ()
}

let printModel = 
    async {
        let! _ = accessLVar modelLVar <| fun model ->
            for kv in model.policy do
                let obs = kv.Key
                let act = kv.Value
                printfn "%s" <| "[" + obs.ToString() + "] -> " + act.ToString()
        ()
    }