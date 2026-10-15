using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M3: tells the client what stands on every cell of the map in view, one character a cell (the
    /// codes are in protocol/PROTOCOL.md), so it can build the map in blocks, and every tree and
    /// bush on it (<c>plants</c>), so it can stand a model on each. Sent when the bridge
    /// connects, when the map in view changes and when anything changes; checked once a second
    /// of real time, paused or not.
    /// </summary>
    public static class TerrainReport
    {
        private const float CheckSeconds = 1f;

        private static Map sentMap;
        private static string sentCells;
        private static string sentPlants;
        private static float lastCheck = -1f;

        /// <summary>Forget what was sent, so the next <see cref="Poll"/> sends it all again.</summary>
        public static void Reset()
        {
            sentMap = null;
            sentCells = null;
            sentPlants = null;
            lastCheck = -1f;
        }

        /// <summary>The <c>terrain</c> and <c>plants</c> lines with something new to send.</summary>
        public static List<string> Poll()
        {
            var lines = new List<string>();
            Map map = PawnSnapshot.MapInView();
            if (map == null) return lines;
            float now = Time.realtimeSinceStartup;
            if (map == sentMap && lastCheck >= 0f && now - lastCheck < CheckSeconds) return lines;
            lastCheck = now;
            bool newMap = map != sentMap;
            sentMap = map;
            string cells = Cells(map);
            if (newMap || cells != sentCells)
            {
                sentCells = cells;
                lines.Add(Json.Msg("terrain")
                    .Raw("size", "[" + map.Size.x + "," + map.Size.z + "]")
                    .Str("cells", cells)
                    .ToString());
            }
            string plants = Plants(map);
            if (newMap || plants != sentPlants)
            {
                sentPlants = plants;
                lines.Add(Json.Msg("plants").Raw("plants", plants).ToString());
            }
            return lines;
        }

        /// <summary>Row by row from the south edge, west to east.</summary>
        private static string Cells(Map map)
        {
            var sb = new StringBuilder(map.Size.x * map.Size.z);
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                    sb.Append(Code(new IntVec3(x, 0, z), map));
            return sb.ToString();
        }

        /// <summary>What stands on the cell, unless it is still being built (a frame): the client leaves those out.</summary>
        public static Building Edifice(IntVec3 cell, Map map)
        {
            Building edifice = map.edificeGrid[cell];
            return edifice is Frame || edifice?.def.IsFrame == true ? null : edifice;
        }

        /// <summary>The cell's code: what stands on it, else its ground.</summary>
        public static char Code(IntVec3 cell, Map map)
        {
            Building edifice = Edifice(cell, map);
            if (edifice != null)
            {
                ThingDef def = edifice.def;
                if (def.IsDoor) return '+';
                // M15: smoothed stone walls are walls (their def is no longer natural rock, so
                // they were a 2 block machine, short of the ceiling).
                if (def.building?.unsmoothedThing != null) return 'W';
                if (def.building != null && def.building.isNaturalRock) return '#';
                // Collapsed rocks and the like: rock, mined like it.
                if (def.mineable && def.passability == Traversability.Impassable) return '#';
                if (def == ThingDefOf.Wall || def.graphicData != null && (def.graphicData.linkFlags & LinkFlags.Wall) != 0)
                    return 'W';
                // M17: fences hold people back (RimWorld's pathing does), modelled from `buildings`.
                if (def.building != null && def.building.isFence) return 'M';
                // Machines, turrets, solar panels: solid, but modelled from `buildings`.
                if (def.fillPercent >= 0.99f || def.passability == Traversability.Impassable) return 'M';
                if (def.fillPercent >= 0.2f) return 'c';
            }
            Plant plant = cell.GetPlant(map);
            if (plant?.def?.plant != null && plant.def.plant.IsTree) return 'T';
            TerrainDef ground = map.terrainGrid.TerrainAt(cell);
            if (ground == null) return '.';
            if (ground.IsWater) return ground.passability == Traversability.Impassable ? 'O' : '~';
            if (ground.layerable || ground.bridge) return '=';
            // Grass and bushes: anything low growing wild on the ground. Not crops: they stand
            // as their own pictures, not with grass about them (M15).
            if (plant?.def?.plant != null && !plant.def.plant.Sowable) return 'g';
            if (ground.defName.Contains("Sand")) return ',';
            if (ground.fertility <= 0f) return ':';
            return '.';
        }

        /// <summary>Every tree and bush, as a JSON array of <c>{x, z, kind, growth}</c>.</summary>
        private static string Plants(Map map)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
            {
                if (!(thing is Plant plant) || plant.def.plant == null || !thing.Spawned) continue;
                string kind = KindOf(plant);
                if (kind == null) continue;
                if (!first) sb.Append(',');
                first = false;
                float growth = Mathf.Round(Mathf.Clamp01(plant.Growth) * 10f) / 10f;
                sb.Append("{\"x\":").Append(plant.Position.x)
                    .Append(",\"z\":").Append(plant.Position.z)
                    .Append(",\"kind\":\"").Append(kind)
                    .Append("\",\"growth\":").Append(growth.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// What a plant looks like, as PROTOCOL.md's kinds; null for grass. A crop is its own
        /// kind (<c>crop_</c> and its def), <c>_young</c> while RimWorld draws it unripe, so it
        /// stands as RimWorld's own picture of it (M15: crops in planters were not drawn).
        /// </summary>
        internal static string KindOf(Plant plant)
        {
            string name = plant.def.defName;
            if (plant.def.plant.Sowable && !plant.def.plant.IsTree)
            {
                bool young = plant.def.plant.immatureGraphic != null && !plant.HarvestableNow;
                return "crop_" + name + (young ? "_young" : "");
            }
            if (!plant.def.plant.IsTree)
            {
                bool bush = name.Contains("Bush") || name.Contains("Bramble") || name.Contains("Shrub")
                    || name.Contains("Berry") || name.Contains("Agave");
                return bush ? "bush" : null;
            }
            if (plant.LeaflessNow || name.Contains("Burned")) return "dead";
            if (name.Contains("Pine") || name.Contains("Cypress")) return "pine";
            if (name.Contains("Birch") || name.Contains("Poplar")) return "birch";
            if (name.Contains("Palm") || name.Contains("Cocoa") || name.Contains("Bamboo")) return "palm";
            if (name.Contains("Cactus")) return "cactus";
            return "oak";
        }
    }
}
