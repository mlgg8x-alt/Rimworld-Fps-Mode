using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M4: tells IW4L about every explosion on the map in view as it starts, so MW2 can show and
    /// sound it. RimWorld applies the damage itself.
    /// </summary>
    [HarmonyPatch(typeof(Explosion), nameof(Explosion.StartExplosion))]
    internal static class ExplosionReport
    {
        private static void Postfix(Explosion __instance)
        {
            System.Action<string> send = ShotReport.Send;
            if (send == null || __instance.Map == null || __instance.Map != PawnSnapshot.MapInView()) return;
            IntVec3 cell = __instance.Position;
            send(Json.Msg("explosion")
                .Num("x", cell.x + 0.5f)
                .Num("z", cell.z + 0.5f)
                .Num("radius", __instance.radius)
                .Str("damage", __instance.damType?.defName ?? "Bomb")
                .ToString());
        }
    }
}
