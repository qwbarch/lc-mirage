module Mirage.Core.Audio.Network.Receiver

open FSharpPlus
open UnityEngine
open NAudio.Wave
open Microsoft.FSharp.Data.UnitSystems.SI.UnitNames
open System
open System.Threading.Tasks
open Mirage.Core.Audio.Data
open Mirage.PluginInfo
open Mirage.Core.Audio.Format
open Mirage.Core.Logger

/// <summary>
/// Receive audio from <b>AudioSender</b>.
/// </summary>
type AudioReceiver =
    private
        {   audioSource: AudioSource
            pcmHeader: PcmHeader
            decompressor: IMp3FrameDecompressor
            mutable startTime: int64
            mutable timeoutEnabled: bool
            mutable stopped: bool
        }

/// <summary>
/// Stop the audio receiver. This must be called to cleanup resources.
/// </summary>
let stopReceiver (receiver: AudioReceiver) =
    if not receiver.stopped then
        receiver.stopped <- true
        receiver.audioSource.Stop()
        UnityEngine.Object.Destroy receiver.audioSource.clip
        receiver.audioSource.clip <- null
        dispose receiver.decompressor

/// <summary>
/// Start receiving audio data from the server, and playing it back live.
/// 
/// Note: This will not stop the <b>AudioSource</b> if it's currently playing.
/// You will need to handle that yourself at the callsite.
/// </summary>
let startReceiver (audioSource: AudioSource) (pcmHeader: PcmHeader) : AudioReceiver =
    audioSource.clip <-
        AudioClip.Create(
            pluginId,
            pcmHeader.samples,
            pcmHeader.channels,
            pcmHeader.frequency,
            false
        )
    ignore <| audioSource.clip.SetData(Array.zeroCreate(pcmHeader.samples * pcmHeader.channels), 0)
    let waveFormat =
        new Mp3WaveFormat(
            pcmHeader.frequency,
            pcmHeader.channels,
            pcmHeader.blockSize,
            pcmHeader.bitRate
        )
    {   audioSource = audioSource
        pcmHeader = pcmHeader
        decompressor = new AcmMp3FrameDecompressor(waveFormat)
        startTime = 0
        timeoutEnabled = false
        stopped = false
    }

/// <summary>
/// Set the audio receiver frame data, and play it if the audio source hasn't started yet.
/// </summary>
let setFrameData (receiver: AudioReceiver) (frameData: FrameData) =
    try
        if not <| isNull receiver.audioSource.clip then
            let pcmData = decompressFrame receiver.decompressor frameData.rawData
            if pcmData.Length > 0 then
                ignore <| receiver.audioSource.clip.SetData(pcmData, frameData.sampleIndex)
            if not receiver.audioSource.isPlaying then
                receiver.audioSource.Play()
            receiver.startTime <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    with | error ->
        logError $"Failed to set frame data: {error}"
        stopReceiver receiver

/// <summary>
/// Set a timeout for the acceptable amount of time in between <b>setFrameData</b> calls.
/// If the timeout is exceeded, the receiver will stop.
/// </summary>
let startTimeout (receiver: AudioReceiver) (timeout: int<second>) : Task<Unit> =
    task {
        receiver.timeoutEnabled <- true
        let timeoutMs = int64 timeout * 1000L
        receiver.startTime <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        let mutable currentTime = receiver.startTime
        while not receiver.stopped && receiver.timeoutEnabled && currentTime - receiver.startTime < timeoutMs do
            do! Async.Sleep 1000
            currentTime <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        if not receiver.stopped && receiver.timeoutEnabled then
            logError $"AudioReceiver timed out after not receiving frame data for {timeout} seconds."
            stopReceiver receiver
    }

/// <summary>
/// Disable the timeout started by <b>startTimeout</b>.
/// </summary>
let stopTimeout (receiver: AudioReceiver) = receiver.timeoutEnabled <- false