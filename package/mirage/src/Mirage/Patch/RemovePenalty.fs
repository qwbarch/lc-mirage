module Mirage.Patch.RemovePenalty

open HarmonyLib
open Mirage.Core.Config

type RemovePenalty() =
    [<HarmonyPrefix>]
    [<HarmonyPatch(typeof<HUDManager>, "ApplyPenalty")>]
    static member ``disable credits penalty on end of round``() = getConfig().enablePenalty