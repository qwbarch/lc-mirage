module Predictor.Domain
open System
open FSharpx.Control
open System.Collections.Generic
open Mirage.Utilities.LVar
open Mirage.Utilities.MVar
open Predictor.DisposableAsync
open FSharpPlus

type TextEmbedding = float32 array

type EitherSpokeHeard = Spoke | Heard

type EntityId = 
    | Guid of Guid
    | Int of int64

type EntityClass = EntityId

type AudioInfo = 
    {   fileId: Guid
        duration: int
    }

// Must be const
// Separate spoke and heard inputs to properly handle a user talking with its own mimic.
type SpokeAtom =
    {   text: string
        audioOption: AudioInfo option
        start: DateTime
        // TODO
        // languageId: int32
    }

type HeardAtom =
    {   text: string
        speakerClass: EntityClass
        speakerId: EntityId
        start: DateTime
        // TODO
        // languageId: int32
    }

type VoiceActivityAtom =
    {   time: DateTime
        speakerId: EntityId
    }

type GameInput =
    | SpokeAtom of SpokeAtom
    | VoiceActivityAtom of VoiceActivityAtom
    | HeardAtom of HeardAtom
    // | ConsoleAtom of ConsoleAtom

type ActivityAtom =
    | Ping
    | SetInactive

// A type for a middle step between raw GameInputs and an Observation
type GameInputStatistics =
    {   
        spokeQueue: SortedDictionary<DateTime, SpokeAtom>
        heardQueue: SortedDictionary<EntityId, SortedDictionary<DateTime, HeardAtom>>
        voiceActivityQueue: SortedDictionary<EntityId, DateTime>
    }

type StatisticsUpdater = MailboxProcessor<GameInput>
type ObservationGenerator = DisposableAsync

type PartialObservation =
    {   spokeEmbedding: Option<string * TextEmbedding>
        heardEmbedding: Option<string * TextEmbedding>
        lastSpokeDate: DateTime
        lastHeardDate: DateTime
    }
type Observation =
    {   time: DateTime
        spokeEmbedding: Option<string * TextEmbedding>
        heardEmbedding: Option<string * TextEmbedding>
        lastSpoke: int
        lastHeard: int
        // recentSpeakers: List<EntityClass> // TODO
    }

type ObsEmbedding =
    | Value of Option<string * TextEmbedding>
    | Prev

type CompressedObservation =
    {   time: DateTime
        spokeEmbedding: ObsEmbedding
        heardEmbedding: ObsEmbedding
        lastSpoke: int
        lastHeard: int
    }
    override this.ToString() =
        let strings = List<string>()
        strings.Add(this.time.ToString() + " " + this.time.Millisecond.ToString())
        match this.spokeEmbedding with
        | Prev -> strings.Add "Previous"
        | Value None -> strings.Add "None"
        | Value (Some (text, _)) -> strings.Add("Spoke: " + text)
        match this.heardEmbedding with
        | Prev -> strings.Add "Previous"
        | Value None -> strings.Add "None"
        | Value (Some (text, _)) -> strings.Add("Heard: " + text)
        String.concat ", " strings

type CompressedObservationFileFormat =
    {   time: DateTime
        spokeEmbedding: ObsEmbedding
        heardEmbedding: ObsEmbedding
        lastSpoke: int
        lastHeard: int
    }

type AudioResponse =
    {   fileId: Guid
        embedding: Option<string * TextEmbedding>
        duration: int
    }
    override this.ToString() =
        match this.embedding with
        | None -> "None"
        | Some (text, _) -> sprintf "\"%s\"" text


type QueueActionInfo = 
    { action: AudioResponse
      delay: int }
    override this.ToString() = this.action.ToString()

type FutureAction =
    | NoAction
    | QueueAction of QueueActionInfo
    override this.ToString() =
        match this with
        | NoAction -> "NoAction"
        | QueueAction q -> "QueueAction " + q.ToString()

type Policy = SortedDictionary<DateTime, CompressedObservation * FutureAction>

type PolicyUpdateMessage = DateTime * CompressedObservation * FutureAction
type MimicPolicyUpdater = AutoCancelAgent<PolicyUpdateMessage>
type FutureActionGenerator = DisposableAsync
type MimicData =
    {   mimicClass: Guid // Equal to the id of the person that this mimic is mimicking
        killSignal: MVar<int>
        sendMimicText: Guid -> unit
        internalPolicy: LVar<Policy>

        policyUpdater: MimicPolicyUpdater

        currentStatistics: LVar<GameInputStatistics>
        notifyUpdateStatistics: MVar<int>
        statisticsUpdater: StatisticsUpdater

        observationChannel: LVar<DateTime -> Observation>
        observationGenerator: ObservationGenerator

        futureActionGenerator: FutureActionGenerator
    }

type LearnerMessageHandler = AutoCancelAgent<GameInput>
type ActivityHandler = AutoCancelAgent<ActivityAtom>
type LearnerAccess =
    {   gameInputHandler: LearnerMessageHandler
        activityHandler: ActivityHandler
        gameInputStatisticsLVar: LVar<GameInputStatistics>
    }

type Model =
    {   policy: Policy
        // Store some helper data to do some operations faster
        mutable lastSpokeEncoding: Option<string * TextEmbedding> option
        mutable lastHeardEncoding: Option<string * TextEmbedding> option
    }

type FilePath = string

type FileFormat =
    {   creationDate: DateTime // We maintain this for an easy way of ordering files
        data: (CompressedObservationFileFormat * FutureAction) array
    }

type FileInfo =
    {   creationDate: DateTime // Sort by DateTime first!!
        name: FilePath
    }
    
type FileState =
    {   dateToFileInfo: Dictionary<DateTime, FileInfo> // Any observation datetime to the file that stores that observation
        fileToData: Dictionary<FilePath, (CompressedObservationFileFormat * FutureAction) array>
        files: SortedSet<FileInfo>
    }

type FileMessage =
    | Add of Observation * FutureAction
    | Update of DateTime * FutureAction

type FileHandler = MailboxProcessor<FileMessage>

type RandomSource = MathNet.Numerics.Random.Mcg31m1