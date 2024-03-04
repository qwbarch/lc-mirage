module Embedding

open System
open System.Runtime.InteropServices
open System.Collections.Generic
open Mirage.Utilities.BatchProcessor
open System.Threading

let [<Literal>] dll = "bertlib.dll"


[<DllImport(dll)>]
extern int ping(int x)

[<DllImport(dll)>]
extern int pingStr(int batch_size, IntPtr s)

[<DllImport(dll, EntryPoint = "encode")>]
extern void _EncodeBatch(int batch_size, IntPtr sentences, IntPtr output) // int, char**, float**

let EMBEDDING_SIZE = 384

// Init by running the model once.
let _encodeBatch (sentences: List<string>) =
    let batch_size = sentences.Count
    let sentencesArray = sentences.ToArray()

    let sentencePtrs = Array.map (fun s -> Marshal.StringToCoTaskMemUTF8 s) sentencesArray
    let pinSentencePtrs = GCHandle.Alloc(sentencePtrs, GCHandleType.Pinned)
    let charPtrPtr = Marshal.UnsafeAddrOfPinnedArrayElement(sentencePtrs, 0)

    let outputArray: float32 array = Array.zeroCreate (batch_size * EMBEDDING_SIZE)
    let pinnedOutputArray = GCHandle.Alloc(outputArray, GCHandleType.Pinned)
    let floatPtrPtr = Marshal.UnsafeAddrOfPinnedArrayElement(outputArray, 0)

    _EncodeBatch(batch_size, charPtrPtr, floatPtrPtr)

    let result = List<float32[]>()
    for sentence_i in 0..(batch_size-1) do
        let row = Array.zeroCreate EMBEDDING_SIZE
        for encode_i in 0..(EMBEDDING_SIZE-1) do
            row[encode_i] <- outputArray[sentence_i * EMBEDDING_SIZE + encode_i]
        
        result.Add(row)

    pinnedOutputArray.Free()
    pinSentencePtrs.Free()
    for sentencePtr in sentencePtrs do
        Marshal.FreeCoTaskMem(sentencePtr)

    result


let textEmbedBatchProcessor = createBatchProcessor _encodeBatch
let modelEncodeText = getProcessSingle textEmbedBatchProcessor

let encodeText (s : string) = 
    async {
        if s.Length = 0 then
            return None
        else
            let! embedding = modelEncodeText s
            return Some (s, embedding)
    }

let embeddingSim (x: float32 array) (y: float32 array) =
    let mutable tot = 0.0f
    for i in 0..(EMBEDDING_SIZE-1) do
        tot <- tot + x[i] * y[i]
    float tot