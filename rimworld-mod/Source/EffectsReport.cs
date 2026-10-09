using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M20: RimWorld's lasting effects, for MW2 to show with its own: every fire burning on
    /// the map in view (on the ground or on someone) as <c>fires</c>, and the gas hanging over
    /// it (smoke from smoke poppers and launchers, tox gas, rot stink, deadlife dust) as
    /// <c>gas</c>, a patch of <see cref="Patch"/> cells at a time. Each is sent when it changes,
    /// not more often than its interval.
    /// </summary>
    public static class EffectsReport
    {
        /// <summary>Cells across a gas patch, as the protocol's <c>GAS_PATCH</c>.</summary>
        private const int Patch = 2;
        private const float FireSeconds = 0.25f;
        private const float GasSeconds = 1f;

        private static string sentFires, sentGas;
        private static float firesAt = -10f, gasAt = -10f;

        public static void Reset()
        {
            sentFires = sentGas = null;
            firesAt = gasAt = -10f;
        }

        /// <summary>The <c>fires</c> and <c>gas</c> lines due now.</summary>
        public static IEnumerable<string> Poll()
        {
            Map map = PawnSnapshot.MapInView();
            if (map == null) yield break;
            float now = Time.realtimeSinceStartup;
            if (now - firesAt >= FireSeconds)
            {
                firesAt = now;
                string fires = Fires(map);
                if (fires != sentFires)
                {
                    sentFires = fires;
                    yield return Json.Msg("fires").Raw("fires", fires).ToString();
                }
            }
            if (now - gasAt >= GasSeconds)
            {
                gasAt = now;
                string gas = Gas(map);
                if (gas != sentGas)
                {
                    sentGas = gas;
                    yield return Json.Msg("gas").Raw("gas", gas).ToString();
                }
            }
        }

        /// <summary>Every fire: where it burns (on whoever it burns), and how big.</summary>
        private static string Fires(Map map)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Fire))
            {
                if (!(thing is Fire fire) || !fire.Spawned) continue;
                Vector3 at = fire.parent != null && fire.parent.Spawned ? fire.parent.DrawPos : fire.Position.ToVector3Shifted();
                if (!first) sb.Append(',');
                first = false;
                // A tenth of a cell and of a size is as fine as MW2 needs, and keeps the line
                // the same while a fire flickers.
                sb.Append("{\"x\":").Append(F(at.x)).Append(",\"z\":").Append(F(at.z))
                    .Append(",\"size\":").Append(F(fire.fireSize)).Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>The thickest gas of each patch with any, and which gas it is.</summary>
        private static string Gas(Map map)
        {
            GasGrid grid = map.gasGrid;
            var sb = new StringBuilder("[");
            if (grid == null) return "[]";
            bool first = true;
            IntVec3 size = map.Size;
            var kinds = new[] { GasType.BlindSmoke, GasType.ToxGas, GasType.RotStink, GasType.DeadlifeDust };
            var names = new[] { "smoke", "tox", "rot", "dust" };
            var most = new int[4];
            for (int px = 0; px < size.x; px += Patch)
                for (int pz = 0; pz < size.z; pz += Patch)
                {
                    bool any = false;
                    for (int k = 0; k < 4; k++) most[k] = 0;
                    for (int x = px; x < px + Patch && x < size.x; x++)
                        for (int z = pz; z < pz + Patch && z < size.z; z++)
                        {
                            var cell = new IntVec3(x, 0, z);
                            if (grid.GetDirect(cell) == 0u) continue;
                            any = true;
                            for (int k = 0; k < 4; k++)
                                most[k] = Mathf.Max(most[k], grid.DensityAt(cell, kinds[k]));
                        }
                    if (!any) continue;
                    int best = 0;
                    for (int k = 1; k < 4; k++)
                        if (most[k] > most[best]) best = k;
                    if (most[best] == 0) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    // In steps of 16, so a cloud drifting thinner is not news every second.
                    int density = Mathf.Clamp(most[best] / 16 * 16 + 8, 1, 255);
                    sb.Append("{\"at\":[").Append(px).Append(',').Append(pz).Append("],\"kind\":\"").Append(names[best])
                        .Append("\",\"density\":").Append(density).Append('}');
                }
            return sb.Append(']').ToString();
        }

        private static string F(float v) => (Mathf.Round(v * 10f) / 10f).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
