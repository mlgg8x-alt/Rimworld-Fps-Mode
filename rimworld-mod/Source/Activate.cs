using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimWorldFPS
{
    /// <summary>
    /// M12: E in the client. The played colonist works whatever is nearest in front of them: a door
    /// opens (or shuts) the way RimWorld opens one for a pawn passing through, so it closes
    /// again on its own once nobody stands in it; anything with a switch (lamps, power switches,
    /// heaters) is flicked on or off at once, as if a colonist had been sent to flick it.
    /// M18: someone downed is picked up and carried, as RimWorld's "Carry" order does, and E
    /// again puts them down.
    /// </summary>
    public static class Activate
    {
        /// <summary>How far away something can be worked, in cells.</summary>
        private const float Reach = 1.6f;

        private static readonly AccessTools.FieldRef<CompFlickable, bool> WantOn =
            AccessTools.FieldRefAccess<CompFlickable, bool>("wantSwitchOn");

        /// <summary>A <c>use</c>: work the nearest thing in front of <paramref name="id"/>.</summary>
        public static void Use(string id)
        {
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn == null || pawn.ThingID != id) return;
            Thing target = Nearest(pawn, map);
            // M18: E puts down whoever the colonist carries, unless a door or switch is in reach.
            if (pawn.carryTracker?.CarriedThing is Pawn carried && (target == null || target is Pawn))
            {
                if (pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _))
                    Log.Message($"[RimWorldFPS] {pawn.LabelShort} put down {carried.LabelShort}");
                return;
            }
            if (target is Pawn downed)
            {
                Pick(pawn, downed);
                return;
            }
            if (target is Building_Door door)
            {
                if (door.Open) door.StartManualCloseBy(pawn);
                else if (door.PawnCanOpen(pawn)) door.StartManualOpenBy(pawn);
                else Messages.Message($"{pawn.LabelShort} can't open {door.LabelShort}.", door, MessageTypeDefOf.RejectInput, false);
                return;
            }
            CompFlickable flick = target?.TryGetComp<CompFlickable>();
            if (flick != null)
            {
                flick.DoFlick();
                // So no colonist is sent off to flick it back.
                WantOn(flick) = flick.SwitchIsOn;
                target.Map?.designationManager.TryRemoveDesignationOn(target, DesignationDefOf.Flick);
            }
        }

        /// <summary>M18: the colonist picks up <paramref name="downed"/>, as RimWorld's "Carry" does
        /// for a drafted colonist: they carry them until E puts them down.</summary>
        private static void Pick(Pawn pawn, Pawn downed)
        {
            if (pawn.carryTracker?.CarriedThing != null) return;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                Messages.Message("CannotCarry".Translate(pawn) + ": " + "Incapable".Translate().CapitalizeFirst(), pawn,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            downed.SetForbidden(false, false);
            Job job = JobMaker.MakeJob(JobDefOf.CarryDownedPawnDrafted, downed);
            job.count = 1;
            if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                Log.Message($"[RimWorldFPS] {pawn.LabelShort} picks up {downed.LabelShort}");
        }

        /// <summary>Whether <paramref name="thing"/> is something E works: a door, a switch, or
        /// someone downed to pick up.</summary>
        private static bool Usable(Thing thing, Pawn user) =>
            thing is Building_Door || thing.TryGetComp<CompFlickable>() != null
            || thing is Pawn other && other != user && !other.Dead && (other.Downed || other.IsSelfShutdown());

        /// <summary>The door, switch or downed pawn within reach the colonist faces most squarely,
        /// nearest first.</summary>
        private static Thing Nearest(Pawn pawn, Map map)
        {
            Vector3 at = pawn.DrawPos;
            Vector3 facing = Quaternion.AngleAxis(pawn.Rotation.AsAngle, Vector3.up) * Vector3.forward;
            Thing best = null;
            float bestScore = float.MaxValue;
            int r = Mathf.CeilToInt(Reach);
            for (int x = pawn.Position.x - r; x <= pawn.Position.x + r; x++)
                for (int z = pawn.Position.z - r; z <= pawn.Position.z + r; z++)
                {
                    var cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map)) continue;
                    foreach (Thing thing in cell.GetThingList(map))
                    {
                        if (!Usable(thing, pawn)) continue;
                        // M20: not the door the colonist stands in, which stays open while they
                        // do: the one beyond it (two doors one behind the other).
                        if (thing is Building_Door && thing.OccupiedRect().Contains(pawn.Position)) continue;
                        Vector3 to = thing.TrueCenter() - at;
                        to.y = 0f;
                        float distance = to.magnitude;
                        if (distance > Reach) continue;
                        // Behind the colonist counts double, so what they look at wins.
                        float ahead = distance < 0.01f ? 1f : Vector3.Dot(to / distance, facing);
                        float score = distance * (ahead < 0f ? 2f : 1.5f - ahead * 0.5f);
                        if (score < bestScore) { bestScore = score; best = thing; }
                    }
                }
            return best;
        }
    }

    /// <summary>
    /// M12: how many times each pawn has been hurt, so the client can flash it red as RimWorld does.
    /// A count rather than a time, so a hurt between two snapshots is never missed.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Hurt
    {
        private static readonly System.Collections.Generic.Dictionary<int, int> times =
            new System.Collections.Generic.Dictionary<int, int>();

        public static int Of(Pawn pawn) => times.TryGetValue(pawn.thingIDNumber, out int n) ? n : 0;

        private static void Postfix(Pawn __instance, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f) return;
            times[__instance.thingIDNumber] = Of(__instance) + 1;
        }
    }
}
