using System.Globalization;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M2: tells the client about every bullet fired on the map in view as it leaves the gun, so the client
    /// can draw its tracer. RimWorld has already rolled the shot: a miss flies to a cell other
    /// than the one aimed at, and <c>destination</c> is where the bullet lands. M4: anything
    /// else fired (mortar shells, grenades, rockets) goes as a <c>shell</c>, with its time in
    /// flight, so the client can fly it in and sound it coming. M8: each bullet carries its <c>id</c>,
    /// so the client can say which strike the FPS player (<see cref="DodgeableShots"/>).
    /// </summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[]
    {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef),
    })]
    internal static class ShotReport
    {
        /// <summary>Heights in cells, for the client: about a soldier's shoulder and chest.</summary>
        private const float MuzzleHeight = 1.3f;
        private const float ImpactHeight = 1.0f;

        private static readonly AccessTools.FieldRef<Projectile, Vector3> DestinationOf =
            AccessTools.FieldRefAccess<Projectile, Vector3>("destination");
        private static readonly AccessTools.FieldRef<Projectile, int> TicksToImpactOf =
            AccessTools.FieldRefAccess<Projectile, int>("ticksToImpact");

        /// <summary>Sends lines to the client while connected; set by <see cref="BridgeGameComponent"/>.</summary>
        internal static System.Action<string> Send;

        private static void Postfix(Projectile __instance, Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget, Thing equipment)
        {
            if (Send == null || __instance.Map == null) return;
            // the FPS player's own bullets came from the client in the first place.
            if (PlayerShots.IsLookAlike(__instance)) return;
            if (__instance.Map != PawnSnapshot.MapInView()) return;
            Vector3 to = DestinationOf(__instance);
            if (!(__instance is Bullet))
            {
                float ticksPerSecond = 60f * Mathf.Max(Find.TickManager.TickRateMultiplier, 0.01f);
                ProjectileProperties props = __instance.def.projectile;
                var shell = Json.Msg("shell")
                    .Str("shooter", launcher?.ThingID ?? "")
                    .Str("weapon", __instance.def.defName)
                    .Raw("from", Point(origin.x, MuzzleHeight, origin.z))
                    .Raw("to", Point(to.x, 0f, to.z))
                    .Num("seconds", Mathf.Max(TicksToImpactOf(__instance), 1) / ticksPerSecond)
                    .Raw("arc", props.flyOverhead ? "true" : "false");
                // M20: its own picture, lobbed as RimWorld lobs it, lying for its fuse.
                int tex = Picture(__instance);
                if (tex >= 0)
                    shell.Num("tex", tex).Num("size", Mathf.Max(__instance.def.graphicData.drawSize.x, 0.2f));
                float lob = props.arcHeightFactor;
                float across = new Vector2(to.x - origin.x, to.z - origin.z).magnitude;
                if (lob > across * 0.2f) lob = across * 0.2f;
                if (lob > 0.01f) shell.Num("lob", lob);
                if (props.explosionDelay > 0) shell.Num("fuse", props.explosionDelay / ticksPerSecond);
                // The picture's art goes first (M13).
                string textures = tex >= 0 ? WorldReport.NewTextures() : null;
                if (textures != null) Send(textures);
                Send(shell.ToString());
                return;
            }
            bool hit = usedTarget.HasThing && usedTarget.Thing == intendedTarget.Thing;
            // M8: the client says which of these strike the colonist it plays (DodgeableShots).
            DodgeableShots.Launched(__instance, launcher, origin, to);
            float speed = __instance.def.projectile.SpeedTilesPerTick * 60f * Find.TickManager.TickRateMultiplier;
            var json = Json.Msg("shot")
                .Str("shooter", launcher?.ThingID ?? "")
                .Str("weapon", equipment?.def?.defName ?? __instance.def.defName)
                .Raw("from", Point(origin.x, MuzzleHeight, origin.z))
                .Raw("to", Point(to.x, ImpactHeight, to.z))
                .Str("result", hit ? "hit" : "miss")
                .Str("id", __instance.ThingID);
            if (speed > 0f) json.Num("speed", speed);
            if (hit) json.Str("target", usedTarget.Thing.ThingID);
            Send(json.ToString());
        }

        /// <summary>The cut-out index of a projectile's own picture, -1 without one.</summary>
        private static int Picture(Projectile projectile)
        {
            try
            {
                Material mat = projectile.def.graphicData?.Graphic?.MatSingle;
                return mat == null ? -1 : TextureExport.Of(mat, cutout: true);
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        private static string Point(float x, float y, float z) =>
            "[" + x.ToString("0.###", CultureInfo.InvariantCulture) + ","
            + y.ToString("0.###", CultureInfo.InvariantCulture) + ","
            + z.ToString("0.###", CultureInfo.InvariantCulture) + "]";
    }
}
