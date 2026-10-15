using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M12: the "FPS mode" toggle on every colonist. On drafts them and hands them to the client,
    /// which comes to the front playing them; off hands them back to RimWorld. Tab in the client
    /// comes back here with them still selected and the toggle still on.
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
            if (pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike || !pawn.Spawned) yield break;
            bool controlled = PlayerPawn.Controls(pawn);
            if (icon == null) icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? TexCommand.Draft;
            var toggle = new Command_Toggle
            {
                defaultLabel = "FPS mode",
                defaultDesc = "On: play this colonist yourself, in first person, in the FPS window (they are drafted). "
                    + "Off: hand them back to RimWorld. Tab in the FPS window comes back here without turning it off.",
                icon = icon,
                isActive = () => PlayerPawn.Controls(pawn),
                toggleAction = () =>
                {
                    if (PlayerPawn.Controls(pawn))
                    {
                        PlayerPawn.Release();
                        return;
                    }
                    if (pawn.drafter != null && !pawn.Drafted) pawn.drafter.Drafted = true;
                    PlayerPawn.Control(pawn);
                    // Android: the client is an app of its own; open it.
                    if (ClientLauncher.Android && !BridgeGameComponent.Linked) ClientLauncher.OpenAndroidApp();
                },
            };
            if (!controlled)
            {
                if (!BridgeGameComponent.Linked && !ClientLauncher.Android) toggle.Disable("The FPS client is not connected: use the indicator's Start button in the top right corner.");
                else if (pawn.Downed) toggle.Disable("Downed.");
                else if (pawn.drafter == null || pawn.WorkTagIsDisabled(WorkTags.Violent)) toggle.Disable("Cannot be drafted.");
            }
            yield return toggle;
        }
    }
}
