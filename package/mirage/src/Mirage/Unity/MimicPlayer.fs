module Mirage.Unity.MimicPlayer

open System
open FSharpPlus
open Unity.Netcode
open GameNetcodeStuff
open Mirage.Core.Field
open Mirage.Core.Config
open Mirage.Core.Logger

let private get<'A> = getter<'A> "MimicPlayer"

/// <summary>
/// A component that attaches to an <b>EnemyAI</b> to mimic a player.
/// If the attached enemy is a <b>MaskedPlayerEnemy</b>, this will also copy its visuals.
/// </summary>
[<AllowNullLiteral>]
type MimicPlayer() as self =
    inherit NetworkBehaviour()

    let random = new Random()

    let MimickingPlayer = field()
    let EnemyAI = field()
    let getEnemyAI = get EnemyAI "EnemyAI"

    let logInstance message =
        handleResult <| monad' {
            let! enemyAI = getEnemyAI "logInstance"
            logInfo $"{enemyAI.GetType().Name}({self.GetInstanceID()}) - {message}"
        }

    let randomPlayer () =
        let round = StartOfRound.Instance
        let playerId = random.Next <| round.connectedPlayersAmount + 1
        round.allPlayerScripts[playerId]

    let mimicPlayer (player: PlayerControllerB) (maskedEnemy: MaskedPlayerEnemy) =
        if not (isNull maskedEnemy) then
            maskedEnemy.mimickingPlayer <- player
            maskedEnemy.SetSuit player.currentSuitID
            maskedEnemy.SetEnemyOutside(player.transform.position.y < -80f)
            maskedEnemy.SetVisibilityOfMaskedEnemy()
            if not (isNull player.deadBody) && not player.deadBody.deactivated then
                logInstance $"Player #{maskedEnemy.mimickingPlayer.playerClientId} has died. Redirecting to enemy and attempting to deactivate dead body."
                player.redirectToEnemy <- maskedEnemy
                try player.deadBody.DeactivateBody false
                with | _ -> () // This can fail due to a mod incompatibility.

    let mimicEnemyEnabled (enemyAI: EnemyAI) =
        let config = getConfig()
        match enemyAI with
            | :? DressGirlAI -> false // DressGirlAI sets the mimicking player after choosing who to haunt.
            | :? BaboonBirdAI -> config.enableBaboonHawk
            | :? FlowermanAI -> config.enableBracken
            | :? SandSpiderAI -> config.enableSpider
            | :? DocileLocustBeesAI -> config.enableLocustSwarm
            | :? RedLocustBees -> config.enableBees
            | :? SpringManAI -> config.enableCoilHead
            | :? SandWormAI -> config.enableEarthLeviathan
            | :? MouthDogAI -> config.enableEyelessDog
            | :? ForestGiantAI -> config.enableForestKeeper
            | :? HoarderBugAI -> config.enableHoardingBug
            | :? BlobAI -> config.enableHygrodere
            | :? JesterAI -> config.enableJester
            | :? DoublewingAI -> config.enableManticoil
            | :? NutcrackerEnemyAI -> config.enableNutcracker
            | :? CentipedeAI -> config.enableSnareFlea
            | :? PufferAI -> config.enableSporeLizard
            | :? CrawlerAI -> config.enableThumper
            | _ -> config.enableModdedEnemies

    member this.Awake() =
        setNullable EnemyAI <| this.GetComponent<EnemyAI>()

    member this.Start() =
        // StartMimicking for masked enemies gets run via a patch instead,
        // to ensure the mimickingPlayer is set before other mods try to interact with it.
        if isNull <| this.GetComponent<MaskedPlayerEnemy>() then
            this.StartMimicking()

    member this.StartMimicking() =
        ignore <| monad' {
            if this.IsHost then
                let! enemyAI = getValue EnemyAI
                let maskedEnemy = this.GetComponent<MaskedPlayerEnemy>()
                let! player =
                    if (enemyAI : EnemyAI) :? MaskedPlayerEnemy then
                        if isNull maskedEnemy.mimickingPlayer then Some <| randomPlayer()
                        else Some maskedEnemy.mimickingPlayer
                    else if mimicEnemyEnabled enemyAI then Some <| randomPlayer()
                    else None
                let playerId = int player.playerClientId
                this.MimicPlayer playerId
        }

    /// <summary>
    /// Mimic the given player locally. An attached <b>MimicVoice</b> automatically uses the mimicked player for voices.
    /// </summary>
    member this.MimicPlayer(playerId) =
        let player = StartOfRound.Instance.allPlayerScripts[playerId]
        logInstance $"Mimicking player #{player.playerClientId}"
        setNullable MimickingPlayer player
        mimicPlayer player <| this.GetComponent<MaskedPlayerEnemy>()
        if this.IsHost then
            this.MimicPlayerClientRpc playerId

    [<ClientRpc>]
    member this.MimicPlayerClientRpc(playerId) =
        if not this.IsHost then
            this.MimicPlayer(playerId)

    member this.ResetMimicPlayer() =
        logInstance "No longer mimicking a player."
        setNone MimickingPlayer
        if this.IsHost then
            this.ResetMimicPlayerClientRpc()

    [<ClientRpc>]
    member this.ResetMimicPlayerClientRpc() =
        if not this.IsHost then
            this.ResetMimicPlayer()

    member _.GetMimickingPlayer() = getValue MimickingPlayer

    member this.Update() =
        ignore <| monad' {
            if this.IsHost then
                // Set the mimicking player after the haunting player changes.
                // In singleplayer, the haunting player will always be the local player.
                // In multiplayer, the haunting player will always be the non-local player.
                let! enemyAI = getEnemyAI "Update" 
                if (enemyAI : EnemyAI) :? DressGirlAI then
                    let dressGirlAI = enemyAI :?> DressGirlAI
                    let round = StartOfRound.Instance

                    let rec randomPlayerNotHaunted () =
                        let player = randomPlayer()
                        if player = dressGirlAI.hauntingPlayer then
                            randomPlayerNotHaunted()
                        else
                            int player.playerClientId

                    match (Option.ofObj dressGirlAI.hauntingPlayer, getValue MimickingPlayer) with
                        | (Some hauntingPlayer, Some mimickingPlayer) when hauntingPlayer = mimickingPlayer && round.connectedPlayersAmount > 0 ->
                            this.MimicPlayer <| randomPlayerNotHaunted()
                        | (Some hauntingPlayer, None) ->
                            if round.connectedPlayersAmount = 0 then
                                this.MimicPlayer <| int hauntingPlayer.playerClientId
                            else
                                this.MimicPlayer <| randomPlayerNotHaunted()
                        | (None, Some _) ->
                            this.ResetMimicPlayer()
                        | _ -> ()
        }