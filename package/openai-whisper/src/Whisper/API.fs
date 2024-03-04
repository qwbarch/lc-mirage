module Whisper.API

open FSharpPlus open FSharp.Json
open System
open System.IO
open System.Diagnostics
open System.Text

/// <summary>
/// Context required to interact with whisper.
/// </summary>
type Whisper = private Whisper of Process

type WhisperRequest<'A> =
    {   requestType: string   
        body: Option<'A>
    }

type WhisperResponse<'A> =
    {   response: Option<'A>
        error: Option<string>
    }

/// <summary>
/// Start the whisper process. This should only be invoked once.
/// </summary>
let startWhisper () =
    let whisper = new Process()
    whisper.StartInfo <-
        new ProcessStartInfo(
            FileName = "model/whisper-s2t/main.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        )
    whisper.StartInfo.StandardInputEncoding <- Encoding.UTF8
    whisper.StartInfo.StandardOutputEncoding <- Encoding.UTF8
    ignore <| whisper.Start()
    Whisper whisper

/// <summary>
/// Kill the whisper process. Any attempts to use whisper after this will fail.
/// </summary>
let stopWhisper (Whisper whisper) = whisper.Kill()

// TODO: use a lock since this currently assumes only one thread will send a request and
// then get a response at the same time, which currently IS NOT THREAD SAFE
let private request<'A, 'B> (Whisper whisper) (request: WhisperRequest<'A>) : Result<'B, string> =
    printfn "%s" <| Json.serializeU request
    whisper.StandardInput.Write(Json.serializeU request)
    whisper.StandardInput.Write '\x00'
    whisper.StandardInput.Flush()
    let response = new StringBuilder()
    let mutable running = true
    while running do
        let letter = char <| whisper.StandardOutput.Read()
        if letter = '\x00' then
            running <- false
        else
            ignore <| response.Append letter
    let body = Json.deserialize <| response.ToString()
    printfn "%s" <| response.ToString()
    match body.response, body.error with
        | Some response, None -> Ok response
        | None, Some error -> Error error
        | _, _ -> Error $"Received an unexpected response from whisper-s2t. Response: {response.ToString()}"

let isCudaAvailable whisper =
    request<string, bool> whisper
        {   requestType = "isCudaAvailable" 
            body = None
        }

type InitModelParams =
    {   useCuda: bool
        cpuThreads: int 
        workers: int
    }

type InitModelFullParams =
    {   modelPath: string
        useCuda: bool
        cpuThreads: int 
        workers: int
    }

/// <summary>
/// Initialize the whisper model. This needs to be run at least once for <b>transcribe</b> to work.<br />
/// </summary>
let initModel whisper (modelParams: InitModelParams) =
    let baseDirectory = AppDomain.CurrentDomain.BaseDirectory
    let fullParams =
        {   modelPath = Path.Join(baseDirectory, "model/whisper-base")
            useCuda = modelParams.useCuda
            cpuThreads = modelParams.cpuThreads
            workers = modelParams.workers
        }
    map ignore <| request<InitModelFullParams, string> whisper
        {   requestType = "initModel"
            body = Some fullParams
        }

/// <summary>
/// A batch of samples to transcribe.
/// </summary>
type TranscribeParams =
    {   samplesBatch: list<array<byte>>
        language: string
    }

type Transcription =
    {   text: string
    }

/// <summary>
/// Transcribe the given audio samples into text.
/// </summary>
let transcribe whisper transcribeParams =
    request<TranscribeParams, Transcription> whisper
        {   requestType = "transcribe"
            body = Some transcribeParams
        }