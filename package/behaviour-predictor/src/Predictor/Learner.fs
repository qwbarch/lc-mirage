module Predictor.Learner
open System
open DisposableAsync
open Mirage.Utilities.LVar
open Predictor.MimicPool
open ObservationGenerator
open Predictor.Domain
open Mirage.Utilities.MVar
open FSharpx.Control
open Model
open Config
open Utilities
open System.Linq
open System.Collections.Generic
open FileHandler
open Embedding

let learnerLVar : LVar<LearnerAccess option> = newLVar(None)

let addEmptyObservation
    (fileHandler: FileHandler)
    (observation: Observation) =
    async {
        let! _ = accessLVar modelLVar <| fun model ->
            if not <| model.policy.ContainsKey(observation.time) then
                let compressedObservation : CompressedObservation = 
                    {   time = observation.time
                        spokeEmbedding = toObsEmbedding model.lastSpokeEncoding observation.spokeEmbedding
                        heardEmbedding = toObsEmbedding model.lastHeardEncoding observation.heardEmbedding
                        lastSpoke = observation.lastSpoke
                        lastHeard = observation.lastHeard
                    }

                // Update the model
                model.policy[observation.time] <- (compressedObservation, NoAction)
                match compressedObservation.spokeEmbedding with
                | Prev -> ()
                | Value (newValue) -> model.lastSpokeEncoding <- Some newValue

                match compressedObservation.heardEmbedding with
                | Prev -> ()
                | Value (newValue) -> model.lastHeardEncoding <- Some newValue

                Async.RunSynchronously <| sendUpdateToMimics compressedObservation.time compressedObservation NoAction
                fileHandler.Post <| Add (observation, NoAction)
        ()
    }

let addSpokeResponse 
    (spokeAtom: SpokeAtom) 
    (fileHandler: FileHandler)
    = 
    async {
        // Get the embedding pertaining to only the user response in isolation.
        // Note that this is different from the spoken embedding from the recent statistics
        let! spokeAtomEmbedding = encodeText spokeAtom.text

        let! _ = accessLVar modelLVar <| fun model ->
            let predicate (kv: KeyValuePair<DateTime, CompressedObservation * FutureAction>) = kv.Key <= spokeAtom.start
            try
                let relevantKV = Enumerable.First(model.policy, predicate)
                let (relObs, relFuture) = relevantKV.Value
                let timeDifferenceMillis = timeSpanToMillis <| spokeAtom.start - relObs.time
                if timeDifferenceMillis < 3 * config.MIL_PER_OBS then
                    let queueAction: QueueActionInfo = {
                        action = 
                            {   fileId=spokeAtom.audioOption.Value.fileId 
                                embedding = spokeAtomEmbedding
                                duration=spokeAtom.audioOption.Value.duration 
                            }
                        delay = timeDifferenceMillis
                    }
                    model.policy[relObs.time] <- (relObs, QueueAction queueAction)
                    Async.RunSynchronously <| sendUpdateToMimics relObs.time relObs (QueueAction queueAction)
                    fileHandler.Post <| Update (relObs.time, QueueAction queueAction)
                    ()
            with
            | :? InvalidOperationException -> 
                logInfo $"No observation found {model.policy.Count}" // No observation found
            | _ -> logWarning "Could not find a relevant observation"
            ()
        ()
    }

// Look for either game inputs or responses. Send the data to the right location depending on the response.
let createLearnerMessageHandler 
    (fileHandler: FileHandler)
    (statisticsUpdater: StatisticsUpdater)
    = 
    AutoCancelAgent.Start(fun inbox ->
    let rec loop () =
        async {
            let! gameInput = inbox.Receive()
            // If the person spoke and it corresponds to a saved recording, we update the model.
            match gameInput with
            | SpokeAtom spokeAtom ->
                if spokeAtom.audioOption.IsSome then
                    do! addSpokeResponse spokeAtom fileHandler
            | HeardAtom _ ->
                ()
            | VoiceActivityAtom vaAtom ->
                ()

            // Either way, send it down the line to the statistics updater
            postToStatisticsUpdater statisticsUpdater gameInput
            do! loop()
        }
    loop()
)

let postToLearnerHandler
    (handler: LearnerMessageHandler)
    (gameInput: GameInput) =
    handler.Post(gameInput)

let learnerObservationSampler
    (fileHandler: FileHandler)
    (observationChannel: LVar<DateTime -> Observation>)
    (isActiveLVar: LVar<bool>) =
    repeatAsync config.MIL_PER_OBS <| async {
        let! isActive = readLVar isActiveLVar
        if isActive then
            let timeStart = DateTime.Now
            let! observationProducer = readLVar observationChannel
            do! addEmptyObservation fileHandler <| observationProducer timeStart
    }

let createActivityHandler (isActiveLVar: LVar<bool>) : ActivityHandler = AutoCancelAgent<ActivityAtom>.Start(fun inbox ->
    let rec loop () =
        async {
            let! messageOption = inbox.TryReceive(config.AFK_MILLIS)
            match messageOption with
            | None ->
                let! prevIsActive = writeLVar isActiveLVar false
                if prevIsActive then
                    logInfo "afk detected"
                ()
            | Some Ping ->
                let! _ = writeLVar isActiveLVar true
                ()
            | Some SetInactive ->
                let! prevIsActive = writeLVar isActiveLVar false
                if prevIsActive then
                    logInfo "Set inactive."
                ()
            do! loop()
        }
    loop()
)

let learnerThread 
    (fileHandler: FileHandler) =
    async {
        let isActiveLVar = newLVar(false)
        let activityHandler = createActivityHandler isActiveLVar
        let currentStatistics = newLVar(defaultGameInputStatistics());
        let notifyUpdateStatistics = createEmptyMVar<int>()
        let statisticsUpdater = createStatisticsUpdater currentStatistics notifyUpdateStatistics 
        let statisticsCutoffHandler = createStatisticsCutoffHandler (Guid userId) currentStatistics notifyUpdateStatistics

        let messageHandler = createLearnerMessageHandler fileHandler statisticsUpdater

        let observationChannel = newLVar(insertObsTime defaultPartialObservation)
        let _ : ObservationGenerator = 
            startAsyncAsDisposable <| createObservationGeneratorAsync (Guid userId) currentStatistics notifyUpdateStatistics observationChannel 

        let _ = startAsyncAsDisposable <| learnerObservationSampler fileHandler observationChannel isActiveLVar

        let learner : LearnerAccess =
            {   gameInputHandler = messageHandler
                activityHandler = activityHandler
                gameInputStatisticsLVar = currentStatistics
            }
        let! _ = writeLVar learnerLVar <| Some learner
        ()
    }
