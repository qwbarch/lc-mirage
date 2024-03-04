module Predictor.MimicPool
open Predictor.PolicyController
open Predictor.DisposableAsync
open Predictor.ActionSelector
open Mirage.Utilities.LVar
open Mirage.Utilities.MVar
open Mirage.Utilities.Lock
open Mirage.Utilities.Print
open System.Collections.Generic
open System
open Domain
open ObservationGenerator
open System.Collections.Concurrent
open Predictor.Model
open EmitAction
open Utilities

let mutable USER_CLASS : Guid = Guid.Empty
let mimicsLVar: LVar<Dictionary<Guid, MimicData>> = newLVar <| Dictionary()

let mimicLifetime (id: Guid) (sendMimicText: Guid -> unit) =
    async {
        // Block until the threads have been created.
        // Setup thread
        use killSignal = createEmptyMVar<int>()

        let! initialPolicy = copyModelPolicy
        use internalPolicy = newLVar(initialPolicy)
        use policyUpdater = createPolicyUpdater internalPolicy

        use currentStatistics = newLVar(defaultGameInputStatistics());
        use notifyUpdateStatistics = createEmptyMVar<int>()
        use statisticsUpdater = createStatisticsUpdater currentStatistics notifyUpdateStatistics 
        use statisticsCutoffHandler = createStatisticsCutoffHandler (Guid id) currentStatistics notifyUpdateStatistics

        use observationChannel = newLVar(insertObsTime defaultPartialObservation)
        use observationGenerator : ObservationGenerator = 
            startAsyncAsDisposable <| createObservationGeneratorAsync (Guid id) currentStatistics notifyUpdateStatistics observationChannel 

        use actionEmitter = createActionEmitter sendMimicText
        let sendToActionEmitter (action: FutureAction) = actionEmitter.Post action
        let rngSource = MathNet.Numerics.Random.Mcg31m1()
        use futureActionGenerator : FutureActionGenerator = 
            startAsyncAsDisposable <| createFutureActionGeneratorAsync internalPolicy observationChannel sendToActionEmitter rngSource


        let mimicData: MimicData = {
            mimicClass = USER_CLASS
            killSignal = killSignal
            sendMimicText = sendMimicText
            internalPolicy = internalPolicy
            policyUpdater = policyUpdater
            currentStatistics = currentStatistics
            notifyUpdateStatistics = notifyUpdateStatistics
            statisticsUpdater = statisticsUpdater
            observationChannel = observationChannel
            observationGenerator = observationGenerator
            futureActionGenerator = futureActionGenerator
        }

        // Add to the mimics pool
        let! __ = accessLVar mimicsLVar <| fun mimics ->
            if not <| mimics.ContainsKey id then
                mimics.Add(id, mimicData)

        // Teardown thread. From here on we assume that no other threads have access to mimicData.
        // Wait for the kill signal
        let! __ = readMVar killSignal
        // The policy updater can be safely killed now since this mimic does not exist in mimicData
        ()
    }

let mimicInit (id: Guid) (sendMimicText: Guid -> unit) : unit =
    Async.Start <| mimicLifetime id sendMimicText


// Assume that sendMimicText should remain possible until mimicKill has returned a value.
let mimicKill (id: Guid) =
    let killjob =
        async {
            let! mimicDataOption = accessLVar mimicsLVar (fun mimicsdict ->
                if mimicsdict.ContainsKey id then
                    let mimicData = mimicsdict[id]
                    ignore <| mimicsdict.Remove(id)
                    Some mimicData
                else
                    None
            )
            
            if mimicDataOption.IsNone then
                return false
            else
                let mimicData = mimicDataOption.Value
                let! __ = putMVar mimicData.killSignal 1
                return true
        }
    Async.Start <| exponentialRepeat 200 10 killjob

let sendUpdateToMimics
    (obsTime: DateTime)
    (obs: CompressedObservation)
    (action: FutureAction) =
    async {
        let! _ = accessLVar mimicsLVar <| fun mimics ->
            for kv in mimics do
                let data = kv.Value
                data.policyUpdater.Post((obsTime, obs, action))
                ()
        ()
    }