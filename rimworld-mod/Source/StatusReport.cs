using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// FPS fork: how the played colonist is, for the client's pawn preview (a Pip-Boy style
    /// figure of them): RimWorld's own health of each of their limbs (head, torso, each arm,
    /// each leg; missing ones flagged), pain, bleeding and how long until it kills them, mood,
    /// their capacities, and the injuries and conditions RimWorld shows. Sent as <c>status</c>
    /// when anything about it changes, at most twice a second. Main thread only.
    /// </summary>
    public static class StatusReport
    {
        private const float Seconds = 0.5f;
        private const int MostConditions = 6;

        private static string sent;
        private static float at = -10f;

        public static void Reset()
        {
            sent = null;
            at = -10f;
        }

        public static string Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (now - at < Seconds) return null;
            at = now;
            Pawn pawn = PlayerPawn.Current(PawnSnapshot.MapInView());
            if (pawn?.health?.hediffSet == null)
            {
                sent = null;
                return null;
            }
            string line;
            try
            {
                line = Build(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[RimWorldFPS] could not report " + pawn.LabelShort + "'s health: " + e.Message, 0x51a7);
                return null;
            }
            if (line == sent) return null;
            sent = line;
            return line;
        }

        private static string Build(Pawn pawn)
        {
            HediffSet set = pawn.health.hediffSet;
            var json = Json.Msg("status")
                .Str("pawn", pawn.ThingID)
                .Num("health", Round(pawn.health.summaryHealth?.SummaryHealthPercent ?? 1f))
                .Num("pain", Round(set.PainTotal))
                .Num("bleeding", Round(set.BleedRateTotal));
            if (set.BleedRateTotal > 0.001f)
                json.Num("bleed_hours", Round(HealthUtility.TicksUntilDeathDueToBloodLoss(pawn) / 2500f));
            float? mood = pawn.needs?.mood?.CurLevelPercentage;
            if (mood.HasValue) json.Num("mood", Round(mood.Value));
            json.Raw("capacities", Capacities(pawn));
            json.Raw("parts", Parts(pawn, set));
            json.Raw("conditions", Conditions(set));
            return json.ToString();
        }

        private static string Capacities(Pawn pawn)
        {
            var caps = new Json();
            void Cap(string name, PawnCapacityDef def)
            {
                if (def != null)
                    caps.Num(name, Round(pawn.health.capacities.GetLevel(def)));
            }
            Cap("consciousness", PawnCapacityDefOf.Consciousness);
            Cap("moving", PawnCapacityDefOf.Moving);
            Cap("manipulation", PawnCapacityDefOf.Manipulation);
            Cap("sight", PawnCapacityDefOf.Sight);
            Cap("breathing", PawnCapacityDefOf.Breathing);
            Cap("blood_pumping", PawnCapacityDefOf.BloodPumping);
            return caps.ToString();
        }

        /// <summary>Each limb's health, 0 to 1: its parts' own health, each weighted by how much
        /// of the body it covers, so a lost finger dents an arm and a lost arm empties it.</summary>
        private static string Parts(Pawn pawn, HediffSet set)
        {
            var weight = new Dictionary<string, float>();
            var hurt = new Dictionary<string, float>();
            var root = new Dictionary<string, BodyPartRecord>();
            foreach (BodyPartRecord part in pawn.RaceProps.body.AllParts)
            {
                string region = Region(part, out BodyPartRecord limb);
                if (limb != null && !root.ContainsKey(region)) root[region] = limb;
                float cover = Mathf.Max(part.coverageAbs, 0.001f);
                float max = Mathf.Max(part.def.GetMaxHealth(pawn), 0.01f);
                float health = set.PartIsMissing(part) ? 0f : Mathf.Clamp01(set.GetPartHealth(part) / max);
                weight[region] = (weight.TryGetValue(region, out float w) ? w : 0f) + cover;
                hurt[region] = (hurt.TryGetValue(region, out float h) ? h : 0f) + (1f - health) * cover;
            }
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (KeyValuePair<string, float> region in weight)
            {
                if (!first) sb.Append(',');
                first = false;
                float health = Mathf.Clamp01(1f - hurt[region.Key] / Mathf.Max(region.Value, 0.001f));
                bool missing = root.TryGetValue(region.Key, out BodyPartRecord limb) && set.PartIsMissing(limb);
                sb.Append("{\"part\":\"").Append(region.Key).Append("\",\"health\":")
                    .Append(Round(missing ? 0f : health).ToString("0.##", CultureInfo.InvariantCulture));
                if (missing) sb.Append(",\"missing\":true");
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>Which limb a body part belongs to: <c>head</c> (with the neck), <c>torso</c>,
        /// <c>left_arm</c>, <c>right_arm</c>, <c>left_leg</c>, <c>right_leg</c>. The limb's own
        /// top part (the shoulder, the leg, the head) is <paramref name="limb"/>.</summary>
        private static string Region(BodyPartRecord part, out BodyPartRecord limb)
        {
            for (BodyPartRecord p = part; p != null; p = p.parent)
            {
                BodyPartDef def = p.def;
                if (def == BodyPartDefOf.Head || def == BodyPartDefOf.Neck)
                {
                    limb = p;
                    return "head";
                }
                if (def == BodyPartDefOf.Shoulder || def == BodyPartDefOf.Arm)
                {
                    limb = Top(p, BodyPartDefOf.Shoulder, BodyPartDefOf.Arm);
                    return Side(limb) + "_arm";
                }
                if (def == BodyPartDefOf.Leg)
                {
                    limb = p;
                    return Side(p) + "_leg";
                }
            }
            limb = null;
            return "torso";
        }

        private static BodyPartRecord Top(BodyPartRecord part, BodyPartDef a, BodyPartDef b)
        {
            BodyPartRecord top = part;
            for (BodyPartRecord p = part; p != null; p = p.parent)
                if (p.def == a || p.def == b) top = p;
            return top;
        }

        private static string Side(BodyPartRecord part) =>
            (part.Label ?? "").IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 ? "right" : "left";

        /// <summary>What RimWorld's health tab shows, worst first: injuries with their part,
        /// illnesses, implants.</summary>
        private static string Conditions(HediffSet set)
        {
            var shown = set.hediffs.Where(h => h != null && h.Visible)
                .OrderByDescending(h => h.Bleeding ? 2f + h.BleedRate : 0f)
                .ThenByDescending(h => h.Severity)
                .Take(MostConditions);
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Hediff h in shown)
            {
                if (!first) sb.Append(',');
                first = false;
                var json = new Json().Str("label", h.LabelCap);
                if (h.Part != null) json.Str("part", h.Part.Label);
                if (h.Bleeding) json.Bool("bleeding", true);
                sb.Append(json.ToString());
            }
            return sb.Append(']').ToString();
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;
    }
}
