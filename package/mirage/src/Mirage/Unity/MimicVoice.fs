module Mirage.Unity.MimicVoice

#nowarn "40"

open FSharpPlus
open UnityEngine
open Mirage.Core.Field
open Mirage.Core.Logger
open Mirage.Core.Monad
open Mirage.Core.Audio.Recording
open Mirage.Core.Config
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer

let private get<'A> = getter<'A> "MimicVoice"

/// <summary>
/// A component that attaches to an <b>EnemyAI</b> to mimic a player's voice.
/// </summary>
[<AllowNullLiteral>]
type MimicVoice() as self =
    inherit MonoBehaviour()

    let random = new System.Random()

    let MimicPlayer = field<MimicPlayer>()
    let AudioStream = field<AudioStream>()
    let EnemyAI = field()
    let getMimicPlayer = get MimicPlayer "MimicPlayer"
    let getAudioStream = get AudioStream "AudioStream"
    let getEnemyAI = get EnemyAI "EnemyAI"

    let startVoiceMimic (enemyAI: EnemyAI) =
        let mimicVoice () =
            handleResult <| monad' {
                let methodName = "mimicVoice"
                let! mimicPlayer = getMimicPlayer methodName
                let! audioStream = getAudioStream methodName
                ignore <| monad' {
                    let! player = mimicPlayer.GetMimickingPlayer()
                    let! recording = getRandomRecording random
                    try
                        if player = StartOfRound.Instance.localPlayerController then
                            if player.IsHost then
                                audioStream.StreamAudioFromFile recording
                            else
                                audioStream.UploadAndStreamAudioFromFile(
                                    player.actualClientId,
                                    recording
                                )
                    with | error ->
                        logError $"Failed to mimic voice: {error}"
                }
            }
        let rec runMimicLoop =
            let config = getConfig()
            let delay =
                if enemyAI :? MaskedPlayerEnemy then
                    random.Next(config.imitateMinDelay, config.imitateMaxDelay + 1)
                else
                    random.Next(config.imitateMinDelayNonMasked, config.imitateMaxDelayNonMasked + 1)
            async {
                mimicVoice()
                return! Async.Sleep delay
                return! runMimicLoop
            }
        toUniTask_ self.destroyCancellationToken runMimicLoop

    let mute () =
        handleResult <| monad {
            let! audioStream = getAudioStream "mute"
            audioStream.GetAudioSource().mute <- true
        }

    member this.Awake() =
        setNullable MimicPlayer <| this.gameObject.GetComponent<MimicPlayer>()
        setNullable AudioStream <| this.gameObject.GetComponent<AudioStream>()
        setNullable EnemyAI <| this.gameObject.GetComponent<EnemyAI>()
        let audioStream = this.GetComponent<AudioStream>()
        setNullable AudioStream audioStream
        let audioSource = audioStream.GetAudioSource()
        audioSource.dopplerLevel <- 0f
        audioSource.maxDistance <- 50f
        audioSource.minDistance <- 6f
        audioSource.priority <- 0
        audioSource.spread <- 30f
        audioSource.spatialBlend <- 1f
        audioSource.gameObject.AddComponent<OccludeAudio>().useReverb <- true

    member _.Start() =
        handleResult <| monad' {
            let! enemyAI = getEnemyAI "Start"
            startVoiceMimic enemyAI
        }

    member _.Update() =
        handleResultWith mute <| monad' {
            let methodName = "Update"
            let! audioStream = getAudioStream methodName
            let audioSource = audioStream.GetAudioSource()
            let! mimicPlayer = getMimicPlayer methodName
            let! enemyAI = getEnemyAI methodName
            let localPlayer = StartOfRound.Instance.localPlayerController
            match mimicPlayer.GetMimickingPlayer() with
                | None -> audioSource.mute <- true
                | Some mimickingPlayer ->
                    let isLocalPlayer = mimickingPlayer = localPlayer
                    let isMimicLocalPlayerMuted () =
                        let alwaysMute = getLocalConfig().AlwaysMuteLocalPlayer.Value
                        let muteWhileNotDead =
                            getConfig().muteLocalPlayerVoice
                                && not mimickingPlayer.isPlayerDead
                        isLocalPlayer && (muteWhileNotDead || alwaysMute)
                    let isNotHauntedOrDisappearedDressGirl () =
                        enemyAI :? DressGirlAI && (
                            let dressGirlAI = enemyAI :?> DressGirlAI
                            let isVisible = dressGirlAI.staringInHaunt || dressGirlAI.moveTowardsDestination && dressGirlAI.movingTowardsTargetPlayer
                            not <| dressGirlAI.hauntingLocalPlayer || not isVisible
                        )
                    let maskedEnemyIsHiding () = enemyAI :? MaskedPlayerEnemy && (enemyAI :?> MaskedPlayerEnemy).crouching
                    audioSource.mute <-
                        enemyAI.isEnemyDead
                            || maskedEnemyIsHiding()
                            || isMimicLocalPlayerMuted()
                            || isNotHauntedOrDisappearedDressGirl()
                            || localPlayer.isInsideFactory && enemyAI.isOutside
                            || not localPlayer.isInsideFactory && not enemyAI.isOutside
        }