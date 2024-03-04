module Mirage.Unity.AudioStream

open FSharpPlus
open UnityEngine
open Unity.Netcode
open Cysharp.Threading.Tasks
open NAudio.Wave
open System.IO
open System.Threading
open Microsoft.FSharp.Data.UnitSystems.SI.UnitNames
open Mirage.Core.Logger
open Mirage.Core.Audio.Data
open Mirage.Core.Monad
open Mirage.Unity.Network
open Mirage.Core.Field
open Mirage.Core.Audio.Format
open Mirage.Core.Audio.Network.Sender
open Mirage.Core.Audio.Network.Receiver

let [<Literal>] ReceiverTimeout = 30<second>
let private get<'A> : Getter<'A> = getter "AudioStream"

/// <summary>
/// A component that allows an entity to stream audio to a client, playing the audio back live.
/// </summary>
[<AllowNullLiteral>]
type AudioStream() =
    inherit NetworkBehaviour()

    let AudioSource = field<AudioSource>()

    /// <summary>
    /// <b>Host only.</b><br />
    /// Send audio to receivers from the host.
    /// </summary>
    let AudioSender = field<AudioSender>()

    /// <summary>
    /// <b>Non-host only.</b><br />
    /// Receive audio on non-hosts.
    /// </summary>
    let AudioReceiver = field<AudioReceiver>()
    
    /// <summary>
    /// <b>Non-host only.</b><br />
    /// Send audio to the host to broadcast to enemies.
    /// </summary>
    let AudioUploader = field<AudioSender>()
    
    /// <summary>
    /// <b>Host only.</b><br />
    /// The client id to accept audio bytes from, and
    /// the current upload bytes of compressed audio.
    /// </summary>
    let CurrentUpload = field<uint64 * MemoryStream>()

    let getAudioSource = get AudioSource "AudioSource"
    let getAudioSender = get AudioSender "AudioSender"
    let getAudioReceiver = get AudioReceiver "AudioReceiver"
    let getCurrentUpload = get CurrentUpload "CurrentUpload"

    let canceller = new CancellationTokenSource()

    let stopAudioSender() =
        iter stopSender AudioSender.Value
        setNone AudioSender

    let stopAudioReciever() =
        iter stopReceiver AudioReceiver.Value
        setNone AudioReceiver

    let stopAll () =
        flip iter AudioSource.Value <| fun audioSource ->
            UnityEngine.Object.Destroy audioSource.clip
        stopAudioSender()
        stopAudioReciever()
        iter (dispose << snd) CurrentUpload.Value

    /// <summary>
    /// Play the given audio file locally.
    /// </summary>
    let playAudio (filePath: string) =
        async {
            let! audioClip =
                forkReturn <|
                    async {
                        use audioReader = new WaveFileReader(filePath)
                        return convertToAudioClip audioReader
                    }
            handleResult <| monad' {
                let! audioSource = getAudioSource "playAudio"
                audioSource.Stop()
                UnityEngine.Object.Destroy audioSource.clip
                audioSource.clip <- audioClip
                audioSource.Play()
            }
        }

    /// <summary>
    /// Stream audio from the host to all clients.
    /// </summary>
    let streamAudioFromHost (this: AudioStream) (audioReader: Mp3FileReader)  =
        handleResultWith stopAll <| monad' {
            stopAudioSender()
            let (audioSender, pcmHeader) = startSender this.SendFrameClientRpc this.FinishAudioClientRpc audioReader
            set AudioSender audioSender

            // A better approach is to have the client notify the server when it's ready,
            // but this requires keeping track of the audio state separately for each client.

            // Since I want to only have one audio reader, we wait for a bit and assume the client
            // has initialized its audio clip.

            // If the client is late and hasn't been initialized by the time it starts to receive audio frames,
            // it will play silent noise until it reaches the earliest frame it receives.
            let! audioSender = getAudioSender "streamAudioFromHost"
            toUniTask_ canceller.Token <| async {
                this.InitializeAudioClientRpc pcmHeader
                do! Async.Sleep 1000 // Wait a second for the clients to initialize its audio clip.
                sendAudio audioSender
            }
        }

    member this.Awake() = setNullable AudioSource <| this.gameObject.AddComponent<AudioSource>()

    override _.OnDestroy() =
        try canceller.Cancel()
        with | _ -> ()
        dispose canceller
        stopAll()

    /// <summary>
    /// Get the attached audio source.
    /// </summary>
    member _.GetAudioSource() =
        match AudioSource.Value with
            | None -> invalidOp "AudioStream#GetAudioSource called while AudioSource has not been initialized yet."
            | Some audio -> audio

    /// <summary>
    /// Stream the given audio file to the host and all clients.<br />
    /// Note: This can only be invoked by the host.
    /// </summary>
    member this.StreamAudioFromFile(filePath: string) =
        handleResult <| monad' {
            if this.IsHost then
                toUniTask_ canceller.Token <| async {
                    let! audioReader =
                        forkReturn <| async {
                            use audio = new AudioFileReader(filePath)
                            return compressAudio audio
                        }
                    streamAudioFromHost this audioReader
                    return! playAudio filePath
                }
        }

    /// <summary>
    /// Initialize the client by sending it the required pcm header.<br />
    /// This is followed by the starting the stream on the server.
    /// </summary>
    [<ClientRpc>]
    member private this.InitializeAudioClientRpc(pcmHeader: PcmHeader) =
        handleResultWith stopAll <| monad' {
            if not this.IsHost then
                stopAudioReciever()
                let! audioSource = getAudioSource "InitializeAudioClientRpc"
                let receiver = startReceiver audioSource pcmHeader
                set AudioReceiver receiver
                startTimeout receiver ReceiverTimeout
                    |> _.AsUniTask().Forget()
        }

    /// <summary>
    /// Send audio frame data to the client to play.
    /// </summary>
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member private this.SendFrameClientRpc(frameData: FrameData) =
        handleResult <| monad' {
            if not this.IsHost then
                let! audioReceiver = getAudioReceiver "SendFrameClientRpc"
                setFrameData audioReceiver frameData 
        }

    /// <summary>
    /// Called when audio is finished streaming.<br />
    /// This disables the client timeout to allow it to continue playing all the audio it already has.
    /// </summary>
    [<ClientRpc>]
    member private this.FinishAudioClientRpc() =
        if not this.IsHost then
            iter stopTimeout AudioReceiver.Value

    /// <summary>
    /// Stream the given audio file to the host and all clients.<br />
    /// Audio is only streamed if the client id matches the sender.
    /// Note: This can only be invoked by a non-host.
    /// </summary>
    member this.UploadAndStreamAudioFromFile(clientId: uint64, filePath: string) =
        if this.IsHost then
            logError "This can only be invoked by non-hosts."
        else
            iter stopSender AudioUploader.Value
            setNone AudioUploader
            toUniTask_ canceller.Token <| async {
                let! audioReader =
                    forkReturn <|
                        async {
                            use audioReader = new AudioFileReader(filePath)
                            return compressAudio audioReader
                        }
                this.InitializeUploadServerRpc(clientId, new ServerRpcParams())
                this.UploadFrameServerRpc(audioReader.xingHeader.frame.RawData, new ServerRpcParams())
                let sendFrame frameData = 
                    this.UploadFrameServerRpc(frameData, new ServerRpcParams())
                let onFinish () =
                    this.UploadAudioFinishedServerRpc <| new ServerRpcParams()
                    setNone AudioUploader
                let (sender, _) = startSender (sendFrame << _.rawData) onFinish audioReader
                sendAudio sender
                set AudioUploader sender
            }

    /// <summary>
    /// Initialize the audio upload by sending the server the required pcm header.<br />
    /// </summary>
    [<ServerRpc(RequireOwnership = false)>]
    member private this.InitializeUploadServerRpc(clientId: uint64, serverParams: ServerRpcParams) =
        if this.IsHost && isValidClient this serverParams && clientId = serverParams.Receive.SenderClientId then
            iter (dispose << snd) CurrentUpload.Value
            set CurrentUpload (clientId, new MemoryStream())

    /// <summary>
    /// Initialize the audio upload by sending the server the required pcm header.<br />
    /// </summary>
    [<ServerRpc(RequireOwnership = false)>]
    member private this.UploadFrameServerRpc(frameData: array<byte>, serverParams: ServerRpcParams) =
        ignore <| monad' {
            if this.IsHost && isValidClient this serverParams then
                let! (clientId, uploadStream) = getCurrentUpload "UploadFrameServerRpc"
                if clientId = serverParams.Receive.SenderClientId then
                    uploadStream.Write frameData
        }

    /// <summary>
    /// Broadcast the uploaded audio.
    /// </summary>
    [<ServerRpc(RequireOwnership = false)>]
    member private this.UploadAudioFinishedServerRpc(serverParams: ServerRpcParams) =
        handleResult <| monad' {
            if this.IsHost && isValidClient this serverParams then
                let! (clientId, uploadStream) = getCurrentUpload "StreamAudioFromUploadServerRpc"
                if clientId = serverParams.Receive.SenderClientId then
                    uploadStream.Position <- 0
                    let audioData = uploadStream.ToArray()
                    uploadStream.Dispose()
                    setNone CurrentUpload

                    // Create a new stream for to play directly on the host.
                    use playbackStream = new MemoryStream(audioData)
                    use playbackMp3 = new Mp3FileReader(playbackStream)
                    let playbackWav = decompressAudio playbackMp3
                    let audioClip = convertToAudioClip playbackWav
                    flip iter AudioSource.Value <| fun audioSource ->
                        audioSource.Stop()
                        UnityEngine.Object.Destroy audioSource.clip
                        audioSource.clip <- audioClip
                        audioSource.Play()

                    streamAudioFromHost this <| new Mp3FileReader(new MemoryStream(audioData))
            }