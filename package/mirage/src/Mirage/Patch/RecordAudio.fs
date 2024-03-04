module Mirage.Patch.RecordAudio

open HarmonyLib
open FSharpPlus
open Dissonance
open Dissonance.VAD
open Dissonance.Audio.Capture
open System.IO
open Mirage.Core.Field
open Mirage.Core.Audio.Recording
open Mirage.Core.Config

let private get<'A> (field: Field<'A>) = field.Value

type RecordAudio() =
    static let Recording = field()
    static let RecordingName = field()
    static let Dissonance = field()
    static let mutable roundStarted = false
    static let mutable voiceActivated = false

    // By default, dissonance sends us 10ms audio frames at a time.
    // VAD can be switched off abruptly. By default, we close the recording after 150ms of no VAD detected.
    // TODO: In the future, make this proper and actually keep track of frame time, rather than hardcoding increments.
    // This should also eventually be encapsulated into a data type holding the recording itself, rather than being part of the patcher.
    static let mutable vadDisabledFrames = 0

    // This is a scuffed workaround on tiny audio files. Any recording containing 300ms or less vad detected audio is deleted.
    static let mutable framesWritten = 0

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<DissonanceComms>, "Start")>]
    static member ``store dissonance for later use``(__instance: DissonanceComms) =
        set Dissonance __instance
        let recorder = new RecordAudio()
        __instance.SubscribeToRecordedAudio recorder
        __instance.SubcribeToVoiceActivation recorder

    [<HarmonyPrefix>]
    [<HarmonyPatch(typeof<StartOfRound>, "Awake")>]
    static member ``stop recording when a new round starts``() =
        roundStarted <- false

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<StartOfRound>, "ResetPlayersLoadedValueClientRpc")>]
    static member ``deleting recordings if per-round is enabled``() =
        roundStarted <- true
        if getConfig().deleteRecordingsPerRound then
            deleteRecordings()

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<MenuManager>, "Start")>]
    static member ``delete recording if per-game is enabled``() =
        if not <| getConfig().deleteRecordingsPerRound then
            deleteRecordings()

    interface IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            ignore <| monad' {
                let! dissonance = get Dissonance

                if voiceActivated then
                    vadDisabledFrames <- 0
                    framesWritten <- framesWritten + 1
                else
                    vadDisabledFrames <- vadDisabledFrames + 1

                if roundStarted && isRecording dissonance (vadDisabledFrames <= 15) then
                    let defaultRecording () =
                        framesWritten <- 0
                        let (fileName, recording) = createRecording format
                        set Recording recording
                        set RecordingName fileName
                        recording
                    let recording = Option.defaultWith defaultRecording <| get Recording
                    recording.WriteSamples buffer
                else
                    ignore <| monad' {
                        let! recording = getValue Recording
                        let! filePath = getValue RecordingName
                        dispose recording
                        if framesWritten <= 30 then
                            try
                                File.Delete filePath
                            with | _ -> ()
                        vadDisabledFrames <- 0
                        framesWritten <- 0
                        setNone Recording
                        setNone RecordingName
                    }
            }
        member _.Reset() =
            vadDisabledFrames <- 0
            iter dispose Recording.Value
            setNone Recording
            setNone RecordingName

    interface IVoiceActivationListener with
        member _.VoiceActivationStart() = voiceActivated <- true
        member _.VoiceActivationStop() = voiceActivated <- false