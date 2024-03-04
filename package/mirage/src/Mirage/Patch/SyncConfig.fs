module Mirage.Patch.SyncConfig

open HarmonyLib
open GameNetcodeStuff
open Unity.Netcode
open Mirage.Core.Config

type SyncConfig() =
    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<PlayerControllerB>, "ConnectClientToPlayerObject")>]
    static member ``synchronize config when joining a game``() =
        if NetworkManager.Singleton.IsHost then
            registerHandler RequestSync
        else
            registerHandler ReceiveSync
            requestSync()

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<GameNetworkManager>, "StartDisconnect")>]
    static member ``desynchronize config after leaving the game``() =
        revertSync()