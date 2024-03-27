namespace Mirage

open Dissonance
open UnityEngine
open System
open System.IO
open System.Runtime.CompilerServices
open BepInEx
open BepInEx.Bootstrap
open FSharpPlus
open HarmonyLib
open Netcode
open NAudio.Lame
open LobbyCompatibility.Features
open LobbyCompatibility.Enums
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
[<BepInDependency(LobbyCompatibility.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    /// Initializes support for <a href="https://thunderstore.io/c/lethal-company/p/BMX/LobbyCompatibility/">LobbyCompatibility</a>.
    /// This is a soft dependency, and does not do anything if **LobbyCompatibility** is not present at runtime.
    [<MethodImpl(MethodImplOptions.NoOptimization)>]
    let initLobbyCompatibility () =
        if Chainloader.PluginInfos.ContainsKey LobbyCompatibility.PluginInfo.PLUGIN_GUID then
            // This looks weird, but is required to prevent an error from occuring if LobbyCompatibility is missing.
            // Note: If this isn't defined as a closure, it will still act as a hard dependency.
            let register () = 
                PluginHelper.RegisterPlugin(
                    pluginName,
                    Version.Parse pluginVersion,
                    CompatibilityLevel.Everyone,
                    VersionStrictness.Minor
                )
            register()

    let onError () = logError "Failed to initialize Mirage. Plugin is disabled."

    member this.Awake() =
        handleResultWith onError <| monad' {
            initAsyncLogger()
            initLobbyCompatibility()
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