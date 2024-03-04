module Predictor.ObservationGenerator
open FSharpx.Control
open FSharp.Core
open Predictor.Domain
open System.Collections.Generic
open Mirage.Utilities.LVar
open Mirage.Utilities.MVar
open FSharpPlus
open System
open Embedding
open Config
open Utilities
open DisposableAsync
open System.Linq

let defaultGameInputStatistics () =
    {   spokeQueue = SortedDictionary()
        heardQueue = SortedDictionary()
        voiceActivityQueue = SortedDictionary()
    }

let mutable startDate = DateTime.Now
let defaultPartialObservation : PartialObservation =
    {   spokeEmbedding = None
        heardEmbedding = None
        lastSpokeDate = startDate
        lastHeardDate = startDate
    }

let insertObsTime (obs: PartialObservation) (newTime: DateTime) : Observation = 
    {   time = newTime 
        spokeEmbedding = obs.spokeEmbedding
        heardEmbedding = obs.heardEmbedding
        lastHeard = timeSpanToMillis <| newTime - obs.lastHeardDate
        lastSpoke = timeSpanToMillis <| newTime - obs.lastSpokeDate
    }

let reduceSize (queue: SortedSet<DateTime * 'T>) =
    let cutoff = DateTime.Now.AddMilliseconds(-config.VOICE_BUFFER)
    let mutable reduced = false
    while queue.Count > 1 && fst queue.Min < cutoff do
        reduced <- true
        let _ = queue.Remove(queue.Min)
        ()
    reduced

let createStatisticsUpdater 
    (currentStatistics: LVar<GameInputStatistics>) 
    (notifyUpdate: MVar<int>)
    : StatisticsUpdater = 
    MailboxProcessor<GameInput>.Start(fun inbox ->
        let rec loop () =
            async {
                do! async {
                    // Consume all unprocessed inputs
                    let! item = inbox.Receive()
                    let! __ = accessLVar currentStatistics <| fun stats ->
                        match item with
                        | SpokeAtom spokeAtom ->
                            replaceDict stats.spokeQueue spokeAtom.start spokeAtom
                        | HeardAtom heardAtom ->
                            if not <| stats.heardQueue.ContainsKey heardAtom.speakerId then
                                stats.heardQueue.Add(heardAtom.speakerId, SortedDictionary())
                            replaceDict stats.heardQueue[heardAtom.speakerId] heardAtom.start heardAtom
                        | VoiceActivityAtom vaAtom -> 
                            if (not <| stats.voiceActivityQueue.ContainsKey(vaAtom.speakerId)) || stats.voiceActivityQueue[vaAtom.speakerId] < vaAtom.time then
                                replaceDict stats.voiceActivityQueue vaAtom.speakerId vaAtom.time

                    ignore <| tryPutMVar notifyUpdate 1 
                }
                
                do! loop()
            }
        loop()
    )

let postToStatisticsUpdater
    (statisticsUpdater: StatisticsUpdater)
    (message: GameInput)
    = statisticsUpdater.Post(message)

let statisticsToPartialObservation (entityId: EntityId) (statistics: GameInputStatistics) : Async<PartialObservation> =
    async {
        let spokeAccum = List<string>()
        let heardAccum = List<string>()
        for kv in statistics.spokeQueue do
            spokeAccum.Add(kv.Value.text)
        for kv in statistics.heardQueue do
            for kv2 in kv.Value do
                heardAccum.Add(kv2.Value.text)

        let spokeConcat = String.concat ". " spokeAccum
        let heardConcat = String.concat ". " heardAccum
        // TODO properly do batching so that it is impossible for these two to be in separate BERT runs
        let! embedding = Async.Parallel [encodeText spokeConcat; encodeText heardConcat]
        let spokeEmbedding = embedding[0]
        let heardEmbedding = embedding[1]
        let now = DateTime.Now
        let partialObservation : PartialObservation =
            {   spokeEmbedding = spokeEmbedding
                heardEmbedding = heardEmbedding
                lastSpokeDate = statistics.voiceActivityQueue.GetValueOrDefault(entityId, startDate)
                lastHeardDate = 
                    let heard = Map.remove entityId <| sortedDictToMap statistics.voiceActivityQueue
                    if heard.Count = 0 then
                        startDate
                    else
                        heard |> Map.toSeq |> Seq.map snd |> Seq.max
            }   
        return partialObservation
    }

let createObservationGeneratorAsync 
    (entityId: EntityId)
    (currentStatisticsLVar: LVar<GameInputStatistics>)
    (notifyUpdateStatistics: MVar<int>)
    (observationChannel: LVar<DateTime -> Observation>) : Async<unit> =
        let rec loop () =
            async {
                let! __ = takeMVar notifyUpdateStatistics
                let! statisticsSnapshot = accessLVar currentStatisticsLVar <| fun stats ->
                    {   spokeQueue = SortedDictionary(stats.spokeQueue)
                        heardQueue = deepCopyNestedDict stats.heardQueue
                        voiceActivityQueue = SortedDictionary(stats.voiceActivityQueue)
                    }
                
                let! partialObservation = statisticsToPartialObservation entityId statisticsSnapshot
                let! __ = writeLVar observationChannel (insertObsTime partialObservation)
                do! loop()
            }
        
        loop()

let reduceSpoke 
    (queue: SortedDictionary<DateTime, SpokeAtom>)
    (lastActivity: DateTime) =
    let mutable reduced = false
    let cutoff = DateTime.Now.AddMilliseconds(-config.VOICE_BUFFER)
    while queue.Count > 1 && queue.First().Key < cutoff do
        let _ = queue.Remove(queue.First().Key)
        reduced <- true
        ()

    if queue.Count > 0 && DateTime.Now > lastActivity.AddMilliseconds(config.VOICE_BUFFER) then
        queue.Clear()
        reduced <- true
    reduced

let reduceHeard
    (queue: SortedDictionary<DateTime, HeardAtom>)
    (lastActivity: DateTime) =
    let mutable reduced = false
    let cutoff = DateTime.Now.AddMilliseconds(-config.VOICE_BUFFER)
    while queue.Count > 1 && queue.First().Key < cutoff do
        let _ = queue.Remove(queue.First().Key)
        reduced <- true
        ()

    if queue.Count > 0 && DateTime.Now > lastActivity.AddMilliseconds(config.VOICE_BUFFER) then
        queue.Clear()
        reduced <- true
    reduced

let reduceHeardQueue
    (stats: GameInputStatistics) =
    let mutable reduced = false
    for kv in stats.heardQueue do
        let lastDateTime =
            if stats.voiceActivityQueue.ContainsKey(kv.Key) then
                stats.voiceActivityQueue[kv.Key]
            else
                DateTime.MinValue
        if reduceHeard kv.Value lastDateTime then
            reduced <- true
    reduced

let createStatisticsCutoffHandler 
    (entityId: EntityId)
    (currentStatisticsLVar: LVar<GameInputStatistics>) 
    (notifyUpdate: MVar<int>) = startAsyncAsDisposable << repeatAsync config.MIL_PER_OBS <| async {
    let! _ = accessLVar currentStatisticsLVar <| fun stats ->
        let reduced = 
            let userLastDateTime =
                if stats.voiceActivityQueue.ContainsKey(entityId) then
                    stats.voiceActivityQueue[entityId]
                else
                    DateTime.MinValue
            let spokeReduce = reduceSpoke stats.spokeQueue userLastDateTime
            let heardReduce = reduceHeardQueue stats
            spokeReduce || heardReduce
        if reduced then
            ignore <| tryPutMVar notifyUpdate 1
        ()
    ()
}