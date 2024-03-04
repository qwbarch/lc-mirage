module Mirage.Core.Logger

open BepInEx
open Mirage.PluginInfo

let private logger = Logging.Logger.CreateLogSource(pluginId)

let internal logInfo (message: string) = logger.LogInfo message
let internal logDebug (message: string) = logger.LogDebug message
let internal logWarning (message: string) = logger.LogWarning message
let internal logError (message: string) = logger.LogError message

/// <summary>
/// If the program results in an error, this function logs the error without rethrowing it.
/// </summary>
let internal handleResultWith (onError: Unit -> Unit) (program: Result<Unit, string>) : Unit =
    match program with
        | Ok _ -> ()
        | Error message ->
            logError message
            onError()

/// <summary>
/// If the program results in an error, this function logs the error without rethrowing it.
/// </summary>
let internal handleResult : Result<Unit, string> -> Unit = handleResultWith id