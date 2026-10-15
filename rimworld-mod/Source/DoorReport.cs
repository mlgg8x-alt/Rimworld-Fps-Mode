using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M11: doors that open as you walk up, as they do in RimWorld. The played colonist standing
    /// next to a door tells that door a pawn is approaching, exactly as RimWorld's own pathing
    /// does, so RimWorld opens it, holds it and closes it again on its own terms. How far each
    /// door has swung goes to the client as <c>doors</c>, which lets the FPS player through once
    /// RimWorld says the way is clear.
    /// </summary>
    public static class DoorReport
    {
        /// <summary>How finely the swing is sent: a door moving reports about this many steps.</summary>
        private const int Steps = 16;
        /// <summary>How far from the played colonist a door hears them coming, in cells.</summary>
        private const int Reach = 1;
        /// <summary>How long a door the played colonist walked up to is asked to stay open, in ticks.</summary>
        private const int HoldTicks = 300;

        private static string sent;

        public static void Reset() => sent = null;

        /// <summary>
        /// Opens the doors the played colonist has walked up to, and the <c>doors</c> line when any
        /// door has moved since the last one, else null.
        /// </summary>
        public static string Poll()
        {
            Map map = PawnSnapshot.MapInView();
            if (map == null) return null;
            Approach(map, PlayerPawn.Current(map));
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Building_Door door in Doors(map))
            {
                int swing = Swing(door);
                // Every cell a door covers (M15: the 2 cell security door's second cell was left
                // out, so the client showed it open).
                foreach (IntVec3 cell in door.OccupiedRect())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"at\":[").Append(Int(cell.x)).Append(',').Append(Int(cell.z))
                        .Append("],\"open\":").Append(Int(swing))
                        .Append(",\"axis\":").Append(door.Rotation.IsHorizontal ? '1' : '0');
                    // M16: a wide door is one door, its leaves meeting in its middle.
                    CellRect rect = door.OccupiedRect();
                    if (rect.Width > 1 || rect.Height > 1)
                        sb.Append(",\"rect\":[").Append(Int(rect.minX)).Append(',').Append(Int(rect.minZ)).Append(',')
                            .Append(Int(rect.maxX)).Append(',').Append(Int(rect.maxZ)).Append(']');
                    sb.Append('}');
                }
            }
            string doors = sb.Append(']').ToString();
            if (doors == sent) return null;
            sent = doors;
            return Json.Msg("doors").Raw("doors", doors).Num("steps", Steps).ToString();
        }

        /// <summary>
        /// Tells every door beside the played colonist that a pawn is coming, the way RimWorld's
        /// pathing tells a door the pawn walking into it is coming. RimWorld decides whether that
        /// colonist may open it, how fast it swings and when it closes. M20: a door that slows
        /// pawns (any door without power) opens only for a pawn stepping into it, as RimWorld's
        /// pathing opens it by hand: the one the colonist faces, from beside it or from inside
        /// another door (two doors one behind the other stayed shut).
        /// </summary>
        private static void Approach(Map map, Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || Find.TickManager.Paused) return;
            IntVec3 at = pawn.Position;
            IntVec3 ahead = at + pawn.Rotation.FacingCell;
            for (int x = at.x - Reach; x <= at.x + Reach; x++)
                for (int z = at.z - Reach; z <= at.z + Reach; z++)
                {
                    var cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map)) continue;
                    Building_Door door = cell.GetDoor(map);
                    if (door == null) continue;
                    door.Notify_PawnApproaching(pawn, HoldTicks);
                    if (cell == ahead && door.SlowsPawns && !door.Open && door.PawnCanOpen(pawn))
                        door.StartManualOpenBy(pawn);
                }
        }

        /// <summary>How far a door has swung, in whole <see cref="Steps"/>: 0 shut, Steps wide open.</summary>
        private static int Swing(Building_Door door)
        {
            int ticks = door.TicksToOpenNow;
            if (ticks <= 0) return Steps;
            float open = 1f - (float)door.TicksTillFullyOpened / ticks;
            return Mathf.Clamp(Mathf.RoundToInt(open * Steps), 0, Steps);
        }

        /// <summary>Every door standing on the map, whoever built it.</summary>
        private static IEnumerable<Building_Door> Doors(Map map)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
                if (thing is Building_Door door && door.Spawned) yield return door;
        }

        private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
