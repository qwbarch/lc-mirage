module Mirage.Core.Audio.Network.Stream

open System.Collections.Generic
open System.Diagnostics
open FSharpPlus
open FSharpPlus.Data
open NAudio.Wave
open Mirage.Core.Audio.Data

/// <summary>
/// Contains delay timings for streaming audio.
/// </summary>
type private BufferTimer =
    {   // Current seconds (in audio duration) sent to the user.
        currentBuffer: float
        // Maximum seconds (of audio duration) to buffer.
        maximumBuffer: float
        // Holds the timestamp received during the previous frame.
        previousTime: float
    }

let private frequency = float Stopwatch.Frequency / 1000.0

/// <summary>
/// Default parameters for <b>BufferTimer</b>.
/// </summary>
let private defaultTimer =
    {   maximumBuffer = float Stopwatch.Frequency * 2.0 // 2 seconds (of audio duration) buffered.
        currentBuffer = zero
        previousTime = zero
    }

/// <summary>
/// Convenience type for calculating audio buffer delay timings.
/// 
/// Note: The <b>ReaderT</b> environment contains mutable state, being the <b>Mp3FileReader</b> and
/// <b>LinkedList</b>.
/// </summary>
type private AudioBuffer<'A> = ReaderT<Mp3FileReader * LinkedList<float>, StateT<BufferTimer, Async<'A>>>

let private runAudioBuffer (program: AudioBuffer<_>) =
    StateT.run << ReaderT.run program

/// <summary>
/// Store the amount of audio duration that should be delayed, in a buffer.
/// </summary>
let private bufferAudioDelay : AudioBuffer<_> =
    monad' {
        let! (audioReader, delayBuffer) = ask
        let! timer = get
        let currentTime = audioReader.CurrentTime.TotalMilliseconds * frequency
        let delay = currentTime - timer.previousTime
        ignore <| delayBuffer.AddLast delay
        return! put {
            timer with
                previousTime = currentTime
                currentBuffer = timer.currentBuffer + delay
        }
    }

/// <summary>
/// Delay until the desired buffer (of audio duration) is reached.
/// If the buffer is already reached, this will simply do nothing.
/// </summary>
let private delayUntilBufferReached : AudioBuffer<_> =
    monad' {
        let! (_, delayBuffer) = ask
        let! timer = get
        if timer.currentBuffer >= timer.maximumBuffer then
            let delay = delayBuffer.First.Value
            delayBuffer.RemoveFirst()
            let startTime = Stopwatch.GetTimestamp()
            let mutable delayedTime = 0.0
            return! liftAsync <| async {
                while delayedTime < delay do
                    do! Async.Sleep 100
                    delayedTime <- float <| Stopwatch.GetTimestamp() - startTime
            }
            // Async.sleep isn't accurate and can sleep less/more than what we wanted.
            // If it slept more than expected, we'll need to reduce the buffer a bit.
            let multiplier = if delayedTime > delay then 2.0 else 1.0
            return! put {
                timer with
                    currentBuffer = timer.currentBuffer - delayedTime - (delay * multiplier)
            }
    }

/// <summary>
/// Streams audio using the given <b>sendFrame</b> function, internally handling the timing between payloads.
/// Note: This function implicitly disposes the <b>AudioReader</b> when it finishes processing frames.
/// </summary>
/// <param name="audioReader">
/// Source audio to read from. Since <b>streamAudio</b> disposes the audio reader when finished, you should not be using any of its methods after the stream starts.
/// </param>
/// <param name="sendFrame">
/// Function to run whenever frame data is available. A value of <b>None</b> is passed when the stream is over.
/// </param>
let streamAudio (audioReader: Mp3FileReader) (sendFrame: Option<FrameData> -> Async<Unit>) : Async<Unit> =
    let rec processAudio (frame: Mp3Frame) =
        monad' {
            if isNull frame || isNull frame.RawData then
                return! liftAsync <| sendFrame None
            else
                return! liftAsync << sendFrame <| Some
                    {   rawData = frame.RawData
                        sampleIndex = int audioReader.tableOfContents[audioReader.tocIndex - 1].SamplePosition
                    }
                return! bufferAudioDelay
                return! delayUntilBufferReached
                return! processAudio <| audioReader.ReadNextFrame()
        }
    map ignore <|
        runAudioBuffer
            (processAudio <| audioReader.ReadNextFrame())
            (audioReader, new LinkedList<float>())
            defaultTimer