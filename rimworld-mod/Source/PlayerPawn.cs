using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M2: the colonist the FPS player plays as. M12: only the one the player chose with
    /// "Play in first person" on a drafted colonist (<see cref="ControlGizmo"/>); choosing it again,
    /// undrafting it, or it going down hands the client back to nobody, and RimWorld stays in front.
    /// It moves cell by cell in RimWorld but is drawn gliding after the FPS player.
    /// Read and changed on the main thread only.
    /// </summary>
    public static class PlayerPawn
    {
        private static Pawn current;
        /// <summary>The played colonist's own "fire at will", given back when the client lets go of it:
        /// while played, only the FPS player shoots.</summary>
        private static bool currentFireAtWill;

        /// <summary>Where the FPS player is (or as far as RimWorld let the pawn go), and where the
        /// pawn is drawn, gliding after it; x and z only.</summary>
        private static Vector3 target;
        private static Vector3? shown;
        private static int shownFrame = -1;
        /// <summary>How quickly the drawn pawn closes on the FPS player, per second.</summary>
        private const float GlideRate = 15f;

        /// <summary>The pawn the client plays on <paramref name="map"/>, or null.</summary>
        public static Pawn Current(Map map)
        {
            if (current == null || Playable(current, map)) return current;
            Release();
            return null;
        }

        /// <summary>Whether the client plays <paramref name="pawn"/> now.</summary>
        public static bool Controls(Pawn pawn) => pawn != null && pawn == current;

        /// <summary>"Play in first person": the client plays <paramref name="pawn"/> from now on, and comes to
        /// the front.</summary>
        public static void Control(Pawn pawn)
        {
            if (!Playable(pawn, pawn?.Map)) return;
            if (current != pawn) Release();
            current = pawn;
            shown = null;
            if (pawn.drafter != null)
            {
                currentFireAtWill = pawn.drafter.FireAtWill;
                pawn.drafter.FireAtWill = false;
            }
            Log.Message($"[RimWorldFPS] the client now plays {pawn.LabelShort}");
            RimView.ToClient();
        }

        /// <summary>The client lets go of the colonist it plays; RimWorld stays in front.</summary>
        public static void Release()
        {
            if (current == null) return;
            if (current.drafter != null) current.drafter.FireAtWill = currentFireAtWill;
            Log.Message($"[RimWorldFPS] the client no longer plays {current.LabelShort}");
            current = null;
            shown = null;
        }

        private static bool Playable(Pawn pawn, Map map) =>
            pawn != null && map != null && pawn.Spawned && pawn.Map == map && pawn.Drafted && !pawn.Downed
            && pawn.Faction == Faction.OfPlayer;

        /// <summary>
        /// A <c>player_move</c>: walk the pawn towards the FPS player one cell at a time, stopping
        /// at anything a colonist couldn't walk through. When the pawn is held back, the client sees
        /// the gap in the next snapshot and puts the FPS player back on the pawn.
        /// </summary>
        public static void Move(string id, float x, float z, float yaw)
        {
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = Current(map);
            if (pawn == null || pawn.ThingID != id) return;
            IntVec3 goal = CellAt(x, z);
            IntVec3 cell = pawn.Position;
            // Up to a few steps per message; a bigger gap is a jump RimWorld doesn't allow.
            for (int step = 0; step < 4 && cell != goal; step++)
            {
                var next = new IntVec3(cell.x + Math.Sign(goal.x - cell.x), 0, cell.z + Math.Sign(goal.z - cell.z));
                if (!CanStep(cell, next, map)) break;
                cell = next;
            }
            if (cell != pawn.Position)
            {
                pawn.pather?.StopDead();
                pawn.Position = cell;
                pawn.Notify_Teleported(false, true);
            }
            pawn.Rotation = Facing(pawn.Rotation, yaw);
            // Drawn where the FPS player is, or in the middle of the cell RimWorld held it in.
            target = cell == goal ? new Vector3(x, 0f, z) : cell.ToVector3Shifted();
        }

        /// <summary>Degrees past the half way mark between two facings before the pawn turns.</summary>
        private const float TurnMargin = 12f;

        /// <summary>The way the pawn faces for the client's <paramref name="yaw"/>, kept while the yaw
        /// is near the edge of it (M19: looking about half way between two ways flipped the
        /// colonist's picture back and forth, a flicker on the minimap).</summary>
        internal static Rot4 Facing(Rot4 now, float yaw)
        {
            float off = Mathf.Abs(Mathf.DeltaAngle(now.AsAngle, yaw));
            return off <= 45f + TurnMargin ? now : Rot4.FromAngleFlat(yaw);
        }

        private static IntVec3 CellAt(float x, float z) => new IntVec3((int)Math.Floor(x), 0, (int)Math.Floor(z));

        /// <summary>The played pawn's drawn x and z, gliding towards the FPS player once per frame.</summary>
        internal static bool TryDrawn(Pawn pawn, Vector3 drawn, out Vector3 at)
        {
            at = drawn;
            if (pawn != current || pawn == null) return false;
            if (shown == null)
            {
                shown = new Vector3(drawn.x, 0f, drawn.z);
                target = shown.Value;
            }
            if (shownFrame != Time.frameCount)
            {
                shownFrame = Time.frameCount;
                float step = 1f - Mathf.Exp(-GlideRate * Time.unscaledDeltaTime);
                shown = Vector3.Lerp(shown.Value, target, step);
            }
            at.x = shown.Value.x;
            at.z = shown.Value.z;
            return true;
        }

        /// <summary>A step to a neighbouring cell; diagonals can't cut a corner, as in RimWorld.</summary>
        private static bool CanStep(IntVec3 from, IntVec3 to, Map map)
        {
            if (!to.InBounds(map) || !to.Walkable(map)) return false;
            if (from.x != to.x && from.z != to.z)
                return new IntVec3(to.x, 0, from.z).Walkable(map) && new IntVec3(from.x, 0, to.z).Walkable(map);
            return true;
        }
    }

    /// <summary>Draws the played colonist where <see cref="PlayerPawn"/> says, paused or not.</summary>
    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos), MethodType.Getter)]
    internal static class PlayerPawnDrawPos
    {
        private static readonly AccessTools.FieldRef<Pawn_DrawTracker, Pawn> PawnOf =
            AccessTools.FieldRefAccess<Pawn_DrawTracker, Pawn>("pawn");

        private static void Postfix(Pawn_DrawTracker __instance, ref Vector3 __result)
        {
            if (PlayerPawn.TryDrawn(PawnOf(__instance), __result, out Vector3 at)) __result = at;
        }
    }
}
