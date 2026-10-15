using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimWorldFPS
{
    /// <summary>
    /// M23: the weapon in a pawn's hands, as RimWorld draws it on the map
    /// (<c>PawnRenderUtility.DrawEquipmentAndApparelExtras</c>): aimed at what it aims at or
    /// attacks, else carried openly while drafted or fighting, and always for enemies. Also
    /// counts each pawn's melee attacks, so the client swings with each one.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.Notify_MeleeAttackOn))]
    public static class HeldReport
    {
        private static readonly AccessTools.FieldRef<Pawn_DrawTracker, Pawn> DrawnPawn =
            AccessTools.FieldRefAccess<Pawn_DrawTracker, Pawn>("pawn");

        private static readonly Dictionary<string, int> swings = new Dictionary<string, int>();

        public static void Reset() => swings.Clear();

        /// <summary>A melee attack (RimWorld lunges the attacker at its target here).</summary>
        public static void Postfix(Pawn_DrawTracker __instance)
        {
            Pawn pawn = DrawnPawn(__instance);
            if (pawn == null) return;
            swings.TryGetValue(pawn.ThingID, out int count);
            swings[pawn.ThingID] = count + 1;
        }

        /// <summary>How many melee attacks <paramref name="pawn"/> has made, 0 for none.</summary>
        public static int Swings(Pawn pawn) => swings.TryGetValue(pawn.ThingID, out int count) ? count : 0;

        /// <summary>The <c>held</c> object for <paramref name="pawn"/>, or null when RimWorld
        /// shows nothing in its hands.</summary>
        public static string Held(Pawn pawn)
        {
            try
            {
                ThingWithComps weapon = pawn.equipment?.Primary;
                if (weapon == null || pawn.Downed || pawn.Dead || pawn.carryTracker?.CarriedThing != null) return null;
                Job job = pawn.CurJob;
                if (job?.def != null && job.def.neverShowWeapon) return null;
                float? aim = Aim(pawn);
                bool enemy = pawn.Faction != null && pawn.HostileTo(Faction.OfPlayer);
                if (!aim.HasValue && !enemy && !PawnRenderUtility.CarryWeaponOpenly(pawn)) return null;
                Graphic graphic = weapon.Graphic;
                if (graphic == null) return null;
                Material mat = graphic is Graphic_StackCount stacked
                    ? stacked.SubGraphicForStackCount(1, weapon.def).MatSingleFor(weapon)
                    : graphic.MatSingleFor(weapon);
                int tex = TextureExport.Of(mat, cutout: true);
                if (tex < 0) return null;
                var json = new Json()
                    .Num("tex", (long)tex)
                    .Raw("size", "[" + F(graphic.drawSize.x) + "," + F(graphic.drawSize.y) + "]");
                if (aim.HasValue) json.Num("aim", Mathf.Round(aim.Value));
                if (weapon.def.equippedAngleOffset != 0f) json.Num("turn", weapon.def.equippedAngleOffset);
                if (weapon.def.equippedDistanceOffset != 0f) json.Num("reach", weapon.def.equippedDistanceOffset);
                if (weapon.def.IsMeleeWeapon) json.Bool("melee", true);
                return json.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Where the pawn aims, as RimWorld turns the weapon: at its stance's target
        /// while it warms up or cools down from an attack; null when it aims at nothing.</summary>
        private static float? Aim(Pawn pawn)
        {
            if (!(pawn.stances?.curStance is Stance_Busy busy) || busy.neverAimWeapon || !busy.focusTarg.IsValid) return null;
            Vector3 target = busy.focusTarg.HasThing ? busy.focusTarg.Thing.DrawPos : busy.focusTarg.Cell.ToVector3Shifted();
            float? aim = null;
            if ((target - pawn.DrawPos).MagnitudeHorizontalSquared() > 0.001f) aim = (target - pawn.DrawPos).AngleFlat();
            float? over = pawn.CurrentEffectiveVerb?.AimAngleOverride;
            return over ?? aim;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
