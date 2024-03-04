namespace Mirage

open Dissonance
open UnityEngine
open System
open System.IO
open BepInEx
open FSharpPlus
open HarmonyLib
open Netcode
open NAudio.Lame
open Mirage.PluginInfo
open Mirage.Core.Config
open Mirage.Core.Logger
open Mirage.Core.Audio.Recording
open Mirage.Patch.NetworkPrefab
open Mirage.Patch.SyncConfig
open Mirage.Patch.RemovePenalty
open Mirage.Patch.RecordAudio
open Mirage.Patch.SpawnMaskedEnemy

[<BepInPlugin(pluginName, pluginId, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    let onError () = logError "Failed to initialize Mirage. Plugin is disabled."

    member this.Awake() =
        handleResultWith onError <| monad' {
            Logs.SetLogLevel(LogCategory.Recording, LogLevel.Error);
            initNetcodePatcher()
            return! initConfig this.Config
            ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]
            Application.add_quitting deleteRecordings
            let harmony = new Harmony(pluginId)
            iter (unbox<Type> >> harmony.PatchAll) 
                [   typeof<RegisterPrefab>
                    typeof<RecordAudio>
                    typeof<SpawnMaskedEnemy>
                    typeof<SyncConfig>
                    typeof<RemovePenalty>
                ]
        }