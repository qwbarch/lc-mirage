module Mirage.Unity.Network

open Unity.Netcode
open FSharpPlus
open System

/// <summary>
/// Verify if the given <b>ServerRpcParams</b> contains a valid <b>ServerClientId</b>.
/// This is intended to be used with ServerRpc methods that do not require ownership to invoke.
/// </summary>
let isValidClient (behaviour: NetworkBehaviour) (serverParams: ServerRpcParams) =
    behaviour.NetworkManager.ConnectedClients.ContainsKey serverParams.Receive.SenderClientId

let isPrefab<'A when 'A : null> (networkPrefab: NetworkPrefab) =
    not << isNull <| networkPrefab.Prefab.GetComponent<'A>()

let inline private findPrefabFunctor
        (resultType: Type)
        (toFunctor: (NetworkPrefab -> bool) -> List<NetworkPrefab> -> '``Functor<'NetworkPrefab>``)
        (networkManager: NetworkManager)
        : '``Functor<'A>`` =
    let networkPrefabs = networkManager.NetworkConfig.Prefabs.m_Prefabs
    let isTargetPrefab (networkPrefab: NetworkPrefab) =
        not << isNull <| networkPrefab.Prefab.GetComponent resultType
    let getComponent (networkPrefab: NetworkPrefab) =
        networkPrefab.Prefab.GetComponent<'A>()
    List.ofSeq networkPrefabs
        |> toFunctor isTargetPrefab
        |> map getComponent

/// <summary>
/// Find the network prefabs of the given type.
/// </summary>
let findNetworkPrefabs<'A> : NetworkManager -> List<'A> =
    findPrefabFunctor typeof<'A> filter

/// <summary>
/// Find the first network prefab of the given type.
/// </summary>
let findNetworkPrefab<'A> : NetworkManager -> Option<'A> =
    findPrefabFunctor typeof<'A> tryFind