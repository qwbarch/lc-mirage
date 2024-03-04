module Mirage.Core.Audio.Recording

#nowarn "40"

open FSharpPlus
open Dissonance.Audio
open UnityEngine
open System
open System.IO
open Dissonance
open Mirage.Core.Config

/// <summary>
/// The directory to save audio files in.
/// </summray>
let private RecordingDirectory = $"{Application.dataPath}/../Mirage"

/// <summary>
/// Create a recording file with a random name.
/// </summary>
let createRecording format =
    let filePath = $"{RecordingDirectory}/{DateTime.UtcNow.ToFileTime()}.wav"
    let recording = new AudioFileWriter(filePath, format)
    (filePath, recording)

/// <summary>
/// Whether or not samples should still be recorded.<br />
/// If false, the recording should be disposed.
/// </summary>
let isRecording (dissonance: DissonanceComms) (speechDetected : bool) =
    let isPlayerDead =
        not (isNull GameNetworkManager.Instance)
            && not (isNull GameNetworkManager.Instance.localPlayerController)
            && not GameNetworkManager.Instance.localPlayerController.isPlayerDead
    let pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
    let pushToTalkPressed = pushToTalkEnabled && not dissonance.IsMuted
    let voiceActivated = not pushToTalkEnabled && speechDetected
    isPlayerDead && (pushToTalkPressed || voiceActivated)

/// <summary>
/// Delete the recordings of the local player. Any exception found is ignored.
/// </summary>
let deleteRecordings () =
    if not <| getLocalConfig().IgnoreRecordingsDeletion.Value then
        try Directory.Delete(RecordingDirectory, true)
        with | _ -> ()

/// <summary>
/// Get a random recording's file path. If no recordings exist, this will return <b>None</b>.
/// </summary>
let getRandomRecording (random: Random) =
    let recordings =
        try Directory.GetFiles RecordingDirectory
        with | _ -> zero
    if recordings.Length = 0 then None
    else Some recordings[random.Next recordings.Length]