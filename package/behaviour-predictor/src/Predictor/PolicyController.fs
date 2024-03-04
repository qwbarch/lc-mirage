module Predictor.PolicyController
open FSharpx.Control
open Domain
open Mirage.Utilities.LVar

// Updates the policy for each individal mimic
let createPolicyUpdater 
    (internalPolicyLVar: LVar<Policy>) =
    AutoCancelAgent<PolicyUpdateMessage>.Start(fun inbox ->
        let rec loop () =
            async {
                let! (obsTime, obs, action) = inbox.Receive()
                let! _ = accessLVar internalPolicyLVar <| fun internalPolicy ->
                    internalPolicy[obsTime] <- (obs, action)
                do! loop()
            }
        loop()
    )