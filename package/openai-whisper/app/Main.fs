module App

open System
open FSharpPlus
open Whisper.API
open NAudio.Wave
open FSharp.Json
open System.IO

let private printError<'A> (program: Result<'A, String>) : Unit =
    match program with
        | Ok _ -> ()
        | Error message -> printfn "%s" message

[<EntryPoint>]
let main _ =
    printError <| monad' {
        let whisper = startWhisper()
        let! cudaAvailable = isCudaAvailable whisper
        printfn $"cuda available: {cudaAvailable}"
        return!
            initModel whisper
                {   useCuda = cudaAvailable
                    cpuThreads = 4
                    workers = 1
                }

        let audioReader = new WaveFileReader("../jfk.wav")
        let samples = Array.zeroCreate<byte> <| int audioReader.Length
        ignore <| audioReader.Read(samples, 0, samples.Length)
        let! transcription =
            transcribe whisper
                {   samplesBatch = [ samples ]
                    language = "en"
                }
        printfn "%s" <| transcription.ToString()
        //printfn "%s" $"{List.ofArray samples = transcription}"
        printfn "%s" <| Json.serializeU samples

        stopWhisper whisper
    }
    0