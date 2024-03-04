module Whisper.Test.Inference

open NUnit.Framework
open Assertion
open FSharpPlus
open System
open Whisper.API
open Whisper.Foreign

let private printError<'A> (program: Result<'A, string>) : Unit =
    match program with
        | Ok _ -> ()
        | Error message -> printfn "%s" message

[<Test>]
let ``foobar`` () =
    printError <| monad' {
        let! whisper =
            initWhisper $"{AppDomain.CurrentDomain.BaseDirectory}/ggml-tiny.bin" true
                |> Option.toResultWith "Unable to initialize whisper."
        let _ = whisper_full_default_params 0
        ()
    }