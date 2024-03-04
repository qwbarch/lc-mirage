module Predictor.Lib
open System
open Mirage.Utilities.LVar
open System.Collections.Generic
open Predictor.Domain
open Predictor.MimicPool
open Predictor.Learner
open Mirage.Utilities.Async
open ObservationGenerator
open Embedding
open Learner
open FSharpPlus.Internals
open FileHandler
open System.IO
open Model
open Utilities

let initBehaviourPredictor
    (logInfo: string -> unit)
    (logWarning: string -> unit)
    (logError: string -> unit)
    (userId: Guid)
    (fileDir: string)
    (sizeLimit: int64) : Async<unit> =
        async {
            logInfo "Initiating behaviour predictor"
            Utilities.logInfo <- logInfo
            Utilities.logWarning <- logWarning
            Utilities.logError <- logError
            Model.userId <- userId
            let initEncoder = async {
                let! _ = encodeText "init"
                ()
            }

            let fileAsync = async {
                let fileSubDir = "policy/"
                let policyDir = Path.Combine(fileDir, fileSubDir)
                createDirIfDoesNotExist fileDir fileSubDir
                let! fileState = readStoredPolicy policyDir logWarning
                do! loadModel fileState
                let fileHandler = createFileHandler fileState policyDir sizeLimit
                do! learnerThread fileHandler
            }

            let! _ = Async.Parallel [initEncoder; fileAsync]
            ()
        }

let clearAllStorage () = false // TODO

// Clear the policy and statistics. Does not affect any active mimics
// This function probably does not have much use outside of testing so it doesn't matter much.
let clearMemory = 
    async {
        startDate <- DateTime.Now
        let! _ = accessLVar learnerLVar <| fun learnerOption ->
            match learnerOption with
            | None -> ()
            | Some learner ->
                Async.RunSynchronously <| async {
                    let! _ = writeLVar learner.gameInputStatisticsLVar <| defaultGameInputStatistics()
                    let! _ = writeLVar modelLVar <| emptyModel()
                    ()
                }
            ()
        ()
    }

let userRegisterText
    (gameInput: GameInput)
    = Async.Start <| async {
        let! _ = accessLVar learnerLVar <| fun learnerOption ->
            match learnerOption with
            | None -> ()
            | Some learner -> postToLearnerHandler learner.gameInputHandler gameInput
        ()
    }

let mimicRegisterText
    (mimicId: Guid)
    (gameInput: GameInput)
    = Async.Start <| async {
        let! _ = accessLVar mimicsLVar <| fun mimics ->
            if mimics.ContainsKey mimicId then
                let mimicData = mimics[mimicId]
                postToStatisticsUpdater mimicData.statisticsUpdater gameInput
        ()
    }

let userIsActivePing () = Async.Start <| async {
    let! _ = accessLVar learnerLVar <| fun learnerOption ->
        match learnerOption with
        | None -> ()
        | Some learner -> learner.activityHandler.Post(Ping)
    ()
}

let setUserIsInactive () = Async.Start <| async {
    let! _ = accessLVar learnerLVar <| fun learnerOption ->
        match learnerOption with
        | None -> ()
        | Some learner -> learner.activityHandler.Post(SetInactive)
    ()
}