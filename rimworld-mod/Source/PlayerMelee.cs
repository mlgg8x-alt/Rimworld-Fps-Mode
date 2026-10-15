using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// FPS fork: the played colonist's melee attacks. Fire in the client with a melee weapon or
    /// bare hands swings at the pawn in front and sends a <c>player_melee</c>; the colonist makes
    /// that attack in RimWorld with its own melee verbs, so RimWorld rolls the hit, the damage,
    /// the body part and the cooldown, as for any drafted colonist ordered to attack. A target
    /// RimWorld does not count as within reach is not struck.
    /// </summary>
    public static class PlayerMelee
    {
        /// <summary>A <c>player_melee</c>: <paramref name="id"/>, the played colonist, attacks
        /// <paramref name="target"/>.</summary>
        public static void Strike(string id, string target)
        {
            if (Find.TickManager.Paused) return;
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn == null || pawn.ThingID != id || pawn.meleeVerbs == null) return;
            Pawn victim = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.ThingID == target);
            if (victim == null || victim.Dead || victim == pawn) return;
            if (!victim.Position.AdjacentTo8WayOrInside(pawn.Position)) return;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                Messages.Message($"{pawn.LabelShort} is incapable of violence.", pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            pawn.meleeVerbs.TryMeleeAttack(victim);
        }
    }
}
