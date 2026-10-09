using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M12: the button on a drafted colonist that hands them to MW2. The game starts and stays
    /// in RimWorld; "Control in MW2" brings MW2 to the front playing that colonist. Tab in MW2
    /// comes back here with them still selected, and the same button, now "Stop controlling",
    /// lets them go.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    [StaticConstructorOnStartup]
    internal static class ControlGizmo
    {
        private static Texture2D icon;

        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (Gizmo gizmo in gizmos) yield return gizmo;
            Pawn pawn = __instance;
            if (!BridgeGameComponent.Linked || pawn.Faction != Faction.OfPlayer || !pawn.Drafted || pawn.Downed) yield break;
            bool controlled = PlayerPawn.Controls(pawn);
            if (icon == null) icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? TexCommand.Draft;
            yield return new Command_Action
            {
                defaultLabel = controlled ? "Stop controlling" : "Control in MW2",
                defaultDesc = controlled
                    ? "Hand this colonist back to RimWorld. MW2 goes away until you control someone again."
                    : "Play this colonist in MW2, in first person. Tab in MW2 comes back here.",
                icon = icon,
                action = () =>
                {
                    if (controlled) PlayerPawn.Release();
                    else PlayerPawn.Control(pawn);
                },
            };
        }
    }
}
