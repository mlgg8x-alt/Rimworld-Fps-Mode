using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M10: RimWorld's own art for what grows. <see cref="TerrainReport"/> already says where
    /// every tree and bush stands and what kind it is; this says what each kind looks like
    /// (<c>plant_art</c>), so the client can stand RimWorld's own pictures there instead of the client's
    /// tree models. Grass is not sent plant by plant (a map has tens of thousands): the grassy
    /// cells of <c>terrain</c> carry it, and the <c>grass</c> kind here says what it looks like.
    /// Sent when a kind's pictures are first seen, checked once a second of real time.
    /// </summary>
    public static class FloraReport
    {
        private const float CheckSeconds = 1f;
        /// <summary>Pictures kept for one kind: enough variety without a picture per plant.</summary>
        private const int MostPerKind = 4;

        private static readonly Dictionary<string, List<int>> art = new Dictionary<string, List<int>>();
        private static readonly HashSet<string> drawn = new HashSet<string>();
        private static string sent;
        private static float lastCheck = -1f;

        public static void Reset()
        {
            art.Clear();
            drawn.Clear();
            sent = null;
            lastCheck = -1f;
        }

        /// <summary>The <c>plant_art</c> line when a kind's pictures changed, else null.</summary>
        public static string Poll()
        {
            Map map = PawnSnapshot.MapInView();
            if (map == null) return null;
            float now = Time.realtimeSinceStartup;
            if (lastCheck >= 0f && now - lastCheck < CheckSeconds) return null;
            lastCheck = now;
            Look(map);
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (KeyValuePair<string, List<int>> kind in art)
            {
                if (kind.Value.Count == 0) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"kind\":\"").Append(kind.Key).Append("\",\"tex\":[");
                for (int i = 0; i < kind.Value.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(kind.Value[i].ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("]}");
            }
            string kinds = sb.Append(']').ToString();
            if (kinds == sent || kinds == "[]") return null;
            sent = kinds;
            return Json.Msg("plant_art").Raw("kinds", kinds).ToString();
        }

        /// <summary>Saves a picture of each kind of plant growing on the map that has none yet.</summary>
        private static void Look(Map map)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
            {
                if (!(thing is Plant plant) || plant.def?.plant == null || !thing.Spawned) continue;
                string kind = TerrainReport.KindOf(plant) ?? (Low(plant) ? "grass" : null);
                if (kind == null) continue;
                // By kind as well as def: a crop's unripe and ripe pictures are kinds of their own.
                if (!drawn.Add(plant.def.defName + "/" + kind)) continue;
                if (!art.TryGetValue(kind, out List<int> pictures))
                {
                    pictures = new List<int>();
                    art[kind] = pictures;
                }
                if (pictures.Count >= MostPerKind) continue;
                int tex = Of(plant);
                if (tex >= 0) pictures.Add(tex);
            }
        }

        /// <summary>Whether a plant is the low cover RimWorld scatters over grassy ground.</summary>
        private static bool Low(Plant plant) =>
            !plant.def.plant.IsTree && !plant.def.plant.Sowable && plant.def.plant.harvestedThingDef == null;

        /// <summary>A grown plant's picture, cut out; -1 when it has none.</summary>
        private static int Of(Plant plant)
        {
            try
            {
                Graphic graphic = plant.Graphic;
                if (graphic == null) return -1;
                return TextureExport.Of(graphic.MatSingleFor(plant), cutout: true);
            }
            catch
            {
                return -1;
            }
        }
    }
}
