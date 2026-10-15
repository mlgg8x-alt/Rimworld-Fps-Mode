using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// M8: bullets the FPS player can dodge. RimWorld still decides when each shot is fired and
    /// where it is aimed, but it no longer decides whether a bullet strikes the colonist the client
    /// plays: the client flies every bullet and sends <c>player_struck</c> when one meets the FPS
    /// player, who may have stepped aside or ducked behind a wall since. Only then does the
    /// colonist take the bullet's damage, as RimWorld's own bullet would have dealt it.
    /// </summary>
    public static class DodgeableShots
    {
        /// <summary>What a fired bullet does to whoever it strikes.</summary>
        private sealed class Fired
        {
            public ThingDef bullet;
            public ThingDef equipment;
            public Thing launcher;
            public int amount;
            public float penetration;
            public float angle;
        }

        /// <summary>Bullets the client may still report, newest last: enough for any firefight.</summary>
        private const int Remembered = 512;
        private static readonly Dictionary<string, Fired> fired = new Dictionary<string, Fired>();
        private static readonly Queue<string> order = new Queue<string>();

        /// <summary>Remembers a bullet RimWorld just fired, by the id <see cref="ShotReport"/>
        /// sends the client with it.</summary>
        internal static void Launched(Projectile bullet, Thing launcher, Vector3 origin, Vector3 destination)
        {
            if (fired.ContainsKey(bullet.ThingID)) return;
            while (order.Count >= Remembered) fired.Remove(order.Dequeue());
            Vector3 way = (destination - origin).Yto0();
            fired[bullet.ThingID] = new Fired
            {
                bullet = bullet.def,
                equipment = bullet.EquipmentDef,
                launcher = launcher,
                amount = bullet.DamageAmount,
                penetration = bullet.ArmorPenetration,
                angle = way == Vector3.zero ? 0f : Quaternion.LookRotation(way).eulerAngles.y,
            };
            order.Enqueue(bullet.ThingID);
        }

        /// <summary>Whether a RimWorld bullet hitting <paramref name="thing"/> should pass by:
        /// the colonist the client plays, while the client is there to say what strikes it.</summary>
        internal static bool Spares(Thing thing)
        {
            if (!(thing is Pawn pawn) || !BridgeGameComponent.Linked) return false;
            Map map = PawnSnapshot.MapInView();
            return pawn.Map == map && PlayerPawn.Current(map) == pawn;
        }

        /// <summary>A <c>player_struck</c>: bullet <paramref name="shot"/> met the FPS player,
        /// who plays <paramref name="pawn"/>; <paramref name="part"/> is <c>head</c> or <c>body</c>.
        /// Each bullet strikes once.</summary>
        public static void Struck(string pawn, string shot, string part)
        {
            if (!fired.TryGetValue(shot, out Fired bullet)) return;
            fired.Remove(shot);
            Map map = PawnSnapshot.MapInView();
            Pawn victim = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.ThingID == pawn);
            if (victim == null || victim.Dead) return;
            BodyPartRecord head = part == "head"
                ? victim.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Head)
                : null;
            ProjectileProperties projectile = bullet.bullet.projectile;
            Thing launcher = bullet.launcher != null && !bullet.launcher.Destroyed ? bullet.launcher : null;
            victim.TakeDamage(new DamageInfo(projectile.damageDef, bullet.amount, bullet.penetration, bullet.angle,
                launcher, head, bullet.equipment, intendedTarget: victim));
            if (projectile.extraDamages == null) return;
            foreach (ExtraDamage extra in projectile.extraDamages)
            {
                if (victim.Dead || !Rand.Chance(extra.chance)) continue;
                victim.TakeDamage(new DamageInfo(extra.def, extra.amount, extra.AdjustedArmorPenetration(), bullet.angle,
                    launcher, head, bullet.equipment, intendedTarget: victim));
            }
        }
    }

    /// <summary>A RimWorld bullet reaching the colonist the client plays flies on into the ground:
    /// The client says when a bullet strikes the FPS player (<see cref="DodgeableShots.Struck"/>).</summary>
    [HarmonyPatch(typeof(Bullet), "Impact")]
    internal static class DodgeableBulletImpact
    {
        private static void Prefix(Bullet __instance, ref Thing hitThing)
        {
            if (hitThing != null && !PlayerShots.IsLookAlike(__instance) && DodgeableShots.Spares(hitThing))
                hitThing = null;
        }
    }
}
