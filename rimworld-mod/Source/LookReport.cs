using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M7: how each person on the map in view looks, so the client can dress its the client soldier the
    /// same: skin and hair colour and what they wear, outermost last. Sent as <c>looks</c>
    /// when anyone's changes, checked once a second of real time.
    /// </summary>
    public static class LookReport
    {
        private const float CheckSeconds = 1f;
        private static string sent;
        private static Map sentMap;
        private static float lastCheck = -1f;

        public static void Reset()
        {
            sent = null;
            sentMap = null;
            lastCheck = -1f;
        }

        /// <summary>The <c>looks</c> line when something changed, else null.</summary>
        public static string Poll()
        {
            Map map = PawnSnapshot.MapInView();
            if (map == null) return null;
            float now = Time.realtimeSinceStartup;
            if (map == sentMap && lastCheck >= 0f && now - lastCheck < CheckSeconds) return null;
            lastCheck = now;
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.Humanlike || pawn.story == null) continue;
                if (!first) sb.Append(',');
                first = false;
                Look(pawn, sb);
            }
            string looks = sb.Append(']').ToString();
            if (map == sentMap && looks == sent) return null;
            sentMap = map;
            sent = looks;
            return Json.Msg("looks").Raw("looks", looks).ToString();
        }

        private static void Look(Pawn pawn, StringBuilder sb)
        {
            HairDef hair = pawn.story.hairDef;
            sb.Append("{\"id\":\"").Append(pawn.ThingID)
                .Append("\",\"skin\":").Append(Rgb(pawn.story.SkinColor))
                .Append(",\"hair\":").Append(hair == null || hair.noGraphic ? "null" : Rgb(pawn.story.HairColor))
                .Append(",\"wear\":[");
            bool first = true;
            var worn = pawn.apparel?.WornApparel;
            if (worn != null)
            {
                var sorted = new List<Apparel>(worn);
                // Innermost first, as RimWorld draws them.
                sorted.Sort((a, b) => Layer(a).CompareTo(Layer(b)));
                foreach (Apparel apparel in sorted)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Wear(pawn, apparel, sb);
                }
            }
            sb.Append("]}");
        }

        private static void Wear(Pawn pawn, Apparel apparel, StringBuilder sb)
        {
            ApparelProperties props = apparel.def.apparel;
            int tex = -1;
            try
            {
                if (ApparelGraphicRecordGetter.TryGetGraphicApparel(apparel, pawn.story.bodyType, false, out ApparelGraphicRecord record)
                    && record.graphic != null)
                    tex = TextureExport.Of(record.graphic.MatAt(Rot4.South), cutout: true);
            }
            catch
            {
                tex = -1;
            }
            sb.Append("{\"def\":\"").Append(apparel.def.defName)
                .Append("\",\"color\":").Append(Rgb(apparel.DrawColor))
                .Append(",\"tex\":").Append(tex)
                .Append(",\"layer\":\"").Append(LayerName(apparel))
                .Append("\",\"parts\":[");
            bool first = true;
            if (props?.bodyPartGroups != null)
                foreach (BodyPartGroupDef group in props.bodyPartGroups)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(group.defName).Append('"');
                }
            sb.Append("]}");
        }

        private static int Layer(Apparel apparel)
        {
            var layers = apparel.def.apparel?.layers;
            if (layers == null || layers.Count == 0) return 0;
            int most = 0;
            foreach (ApparelLayerDef layer in layers) most = Mathf.Max(most, layer.drawOrder);
            return most;
        }

        private static string LayerName(Apparel apparel)
        {
            var layers = apparel.def.apparel?.layers;
            return layers == null || layers.Count == 0 ? "" : layers[layers.Count - 1].defName;
        }

        private static string Rgb(Color c) =>
            "[" + B(c.r) + "," + B(c.g) + "," + B(c.b) + "]";

        private static string B(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 255f).ToString(CultureInfo.InvariantCulture);
    }
}
