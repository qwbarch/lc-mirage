module Mirage.Patch.NetworkPrefab

open FSharpPlus
open HarmonyLib
open Unity.Netcode
open Mirage.Core.Config
open Mirage.Core.Logger
open Mirage.Unity.MimicVoice
open Mirage.Unity.AudioStream
open Mirage.Unity.Network
open Mirage.Unity.MimicPlayer

let private init<'A when 'A : null and 'A :> EnemyAI> (networkPrefab: NetworkPrefab) =
    let enemyAI = networkPrefab.Prefab.GetComponent<'A>()
    if not <| isNull enemyAI then
        iter (ignore << (enemyAI :> EnemyAI).gameObject.AddComponent)
            [   typeof<AudioStream>
                typeof<MimicPlayer>
                typeof<MimicVoice>
            ]

type RegisterPrefab() =
    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<GameNetworkManager>, "Start")>]
    static member ``register network prefab``(__instance: GameNetworkManager) =
        handleResult <| monad' {
            let networkManager = __instance.GetComponent<NetworkManager>()
            flip iter networkManager.NetworkConfig.Prefabs.m_Prefabs <| fun prefab ->
                if isPrefab<EnemyAI> prefab then
                    init prefab
            let! mirage =
                findNetworkPrefab<MaskedPlayerEnemy> networkManager
                    |> Option.toResultWith "MaskedPlayerEnemy network prefab is missing. This is likely due to a mod incompatibility"
            mirage.enemyType.isDaytimeEnemy <- true
            mirage.enemyType.isOutsideEnemy <- true
        }

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<StartOfRound>, "Start")>]
    static member ``register prefab to spawn list``(__instance: StartOfRound) =
        let networkManager = UnityEngine.Object.FindObjectOfType<NetworkManager>()
        if networkManager.IsHost && not (getConfig().enableNaturalSpawn) then
            let prefabExists enemy = enemy.GetType() = typeof<MaskedPlayerEnemy>
            flip iter (__instance.levels) <| fun level ->
                flip iter (tryFind prefabExists level.Enemies) <| fun spawnable ->
                    ignore <| level.Enemies.Remove spawnable