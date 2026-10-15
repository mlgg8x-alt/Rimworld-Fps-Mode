using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M12: the button on a drafted colonist that hands them to the client. The game starts and stays
    /// in RimWorld; "Play in first person" brings the client to the front playing that colonist. Tab in the client
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
                defaultLabel = controlled ? "Stop controlling" : "Play in first person",
                defaultDesc = controlled
                    ? "Hand this colonist back to RimWorld. The FPS window hides until you play someone again."
                    : "Play this colonist yourself, in first person, in the FPS window. Tab there comes back here.",
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
