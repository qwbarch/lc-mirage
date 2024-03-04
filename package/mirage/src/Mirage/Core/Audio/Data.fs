module Mirage.Core.Audio.Data

open Unity.Netcode
open NAudio.Wave

/// <summary>
/// Represents raw frame data and the sample index it begins at.
/// </summary>
[<Struct>]
type FrameData =
    {   mutable rawData: array<byte>
        mutable sampleIndex: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) : unit =
            serializer.SerializeValue(&this.rawData)
            serializer.SerializeValue(&this.sampleIndex)

/// <summary>
/// All the necessary information for PCM data to be read.
/// </summary>
[<Struct>]
type PcmHeader =
    {   mutable samples: int
        mutable channels: int
        mutable frequency: int
        mutable blockSize: int
        mutable bitRate: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) : unit = 
            serializer.SerializeValue(&this.samples)
            serializer.SerializeValue(&this.channels)
            serializer.SerializeValue(&this.frequency)
            serializer.SerializeValue(&this.blockSize)
            serializer.SerializeValue(&this.bitRate)

/// <summary>
/// Extracts the pcm header information of an <b>Mp3FileReader</b>.
/// </summary>
let getPcmHeader (audioReader: Mp3FileReader) =
    {   samples = int audioReader.totalSamples
        channels = audioReader.Mp3WaveFormat.Channels
        frequency = audioReader.Mp3WaveFormat.SampleRate
        blockSize  = int audioReader.Mp3WaveFormat.blockSize
        bitRate = audioReader.WaveFormat.AverageBytesPerSecond * sizeof<float>
    }