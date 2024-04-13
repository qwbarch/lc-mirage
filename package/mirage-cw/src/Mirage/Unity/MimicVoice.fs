module Mirage.Unity.MimicVoice

#nowarn "40"

open System
open FSharpPlus
open FSharpPlus.Data
open UnityEngine
open Mirage.Core.Monad
open Mirage.Core.Logger
open Mirage.Core.Config
open Mirage.Core.Field
open Mirage.Core.Audio.Recording
open Mirage.Unity.AudioStream
open Mirage.Unity.RpcBehaviour
open Mirage.Unity.MimicPlayer

let mutable internal PlaybackPrefab: GameObject = null
let private get<'A> = getter<'A> "MimicVoice"

[<AllowNullLiteral>]
type MimicVoice() as self =
    inherit RpcBehaviour()

    let random = Random()
    let recordingManager = RecordingManager()
    let Playback = field()

    let MimicPlayer = field<MimicPlayer>()
    let AudioStream = field<AudioStream>()
    let getMimicPlayer = get MimicPlayer "MimicPlayer"
    let getAudioStream = get AudioStream "AudioStream"

    let startVoiceMimic () =
        let mimicVoice () =
            handleResult <| monad' {
                let methodName = "mimicVoice"
                let! mimicPlayer = getMimicPlayer methodName
                let! audioStream = getAudioStream methodName
                ignore << runAsync self.destroyCancellationToken << OptionT.run <| monad {
                    try
                        let! player = OptionT << result <| mimicPlayer.GetMimickingPlayer()
                        if player = Player.localPlayer then
                            let! recording = OptionT <| getRecording recordingManager
                            if self.IsHost then
                                audioStream.StreamAudioFromFile recording
                            else
                                audioStream.UploadAndStreamAudioFromFile(
                                    player.refs.view.ViewID,
                                    recording
                                )
                    with | error ->
                        logError $"Failed to mimic voice: {error}"
                }
            }
        let rec runMimicLoop =
            let config = getConfig()
            let delay = random.Next(config.mimicMinDelay, config.mimicMaxDelay + 1)
            async {
                mimicVoice()
                return! Async.Sleep delay
                return! runMimicLoop
            }
        runAsync self.destroyCancellationToken <| async {
            // Bandaid fix to wait for network objects to instantiate on clients, since Mycelium doesn't handle this edge-case yet.
            return! Async.Sleep 5000
            return! runMimicLoop
        }

    member this.Start() =
        let playback = Object.Instantiate<GameObject> PlaybackPrefab
        playback.transform.parent <- this.transform
        setNullable Playback playback
        playback.SetActive true
        let audioStream = this.GetComponent<AudioStream>()
        audioStream.SetAudioSource <| playback.GetComponent<AudioSource>()
        setNullable AudioStream audioStream
        setNullable MimicPlayer <| this.gameObject.GetComponent<MimicPlayer>()
        startVoiceMimic()

    member this.LateUpdate() =
        // Update the playback component to always be on the same position as the parent.
        // This ensures audio plays from the correct position.
        flip iter Playback.Value <| fun playback ->
        playback.transform.position <- this.transform.position