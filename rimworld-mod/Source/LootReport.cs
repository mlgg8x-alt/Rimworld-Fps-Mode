using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldFPS
{
    /// <summary>
    /// M4: looting. Tells the client what the played colonist has and what lies within its reach
    /// (<c>loot</c>), and applies the client's <c>loot_take</c> and <c>loot_drop</c> with RimWorld's own
    /// calls: equip as <c>JobDriver_Equip</c> does, wear as <c>JobDriver_Wear</c>, carry as
    /// <c>Toils_Haul.TakeToInventory</c>, and drop as the colonist's gizmos do.
    /// Read and changed on the main thread only.
    /// </summary>
    public static class LootReport
    {
        /// <summary>The colonist's own cell and the eight around it.</summary>
        private const float Reach = 1.5f;
        /// <summary>How often the report is rebuilt, real time; sent only when it changed.</summary>
        private const float PollSeconds = 0.25f;

        private static string last;
        private static float lastPoll = -1f;

        public static void Reset()
        {
            last = null;
            lastPoll = -1f;
        }

        /// <summary>The <c>loot</c> line when it changed, otherwise null.</summary>
        public static string Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPoll < PollSeconds) return null;
            lastPoll = now;
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn == null)
            {
                last = null;
                return null;
            }
            var kit = new List<string>();
            foreach (Thing thing in Kit(pawn)) kit.Add(ItemJson(thing));
            var nearby = new List<string>();
            foreach (var (id, label, things) in Sources(pawn))
            {
                var items = things.Select(ItemJson).ToList();
                if (items.Count == 0) continue;
                nearby.Add(new Json().Str("id", id).Str("label", label)
                    .Raw("items", "[" + string.Join(",", items) + "]").ToString());
            }
            string line = Json.Msg("loot")
                .Str("pawn", pawn.ThingID)
                .Num("mass", MassUtility.GearAndInventoryMass(pawn))
                .Num("capacity", MassUtility.Capacity(pawn))
                .Raw("kit", "[" + string.Join(",", kit) + "]")
                .Raw("nearby", "[" + string.Join(",", nearby) + "]")
                .ToString();
            if (line == last) return null;
            last = line;
            return line;
        }

        /// <summary>A <c>loot_take</c>: the nearby thing <paramref name="item"/> to the colonist.</summary>
        public static void Take(string id, string item)
        {
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn == null || pawn.ThingID != id) return;
            foreach (var (_, _, things) in Sources(pawn))
            {
                Thing thing = things.FirstOrDefault(t => t.ThingID == item);
                if (thing == null) continue;
                Taken(pawn, thing);
                last = null;
                return;
            }
        }

        /// <summary>A <c>loot_drop</c>: the colonist's <paramref name="item"/> to its feet.</summary>
        public static void Drop(string id, string item)
        {
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn == null || pawn.ThingID != id) return;
            Thing thing = Kit(pawn).FirstOrDefault(t => t.ThingID == item);
            if (thing == null) return;
            if (thing is ThingWithComps eq && pawn.equipment != null && pawn.equipment.AllEquipmentListForReading.Contains(eq))
                pawn.equipment.TryDropEquipment(eq, out _, pawn.Position, forbid: false);
            else if (thing is Apparel apparel && pawn.apparel != null && pawn.apparel.WornApparel.Contains(apparel))
                pawn.apparel.TryDrop(apparel, out _, pawn.Position, forbid: false);
            else
                pawn.inventory?.innerContainer.TryDrop(thing, pawn.Position, map, ThingPlaceMode.Near, out _);
            thing.def.soundDrop?.PlayOneShot(new TargetInfo(pawn.Position, map));
            last = null;
        }

        private static void Taken(Pawn pawn, Thing thing)
        {
            Map map = pawn.Map;
            Pawn holder = Holder(thing);
            if (thing is ThingWithComps weapon && thing.def.IsWeapon && pawn.equipment != null && !pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                ThingWithComps taken = weapon;
                if (holder != null)
                {
                    if (holder.equipment != null && holder.equipment.AllEquipmentListForReading.Contains(weapon))
                        holder.equipment.Remove(weapon);
                    else
                        holder.inventory?.innerContainer.Remove(weapon);
                }
                else if (weapon.def.stackLimit > 1 && weapon.stackCount > 1)
                    taken = (ThingWithComps)weapon.SplitOff(1);
                else
                    weapon.DeSpawn();
                pawn.equipment.MakeRoomFor(taken);
                pawn.equipment.AddEquipment(taken);
                taken.def.soundInteract?.PlayOneShot(new TargetInfo(pawn.Position, map));
                return;
            }
            if (thing is Apparel apparel && pawn.apparel != null && ApparelUtility.HasPartsToWear(pawn, apparel.def)
                && apparel.PawnCanWear(pawn, ignoreGender: true)
                && (!CompBiocodable.IsBiocoded(apparel) || CompBiocodable.IsBiocodedFor(apparel, pawn)))
            {
                if (holder != null)
                {
                    if (holder.apparel != null && holder.apparel.WornApparel.Contains(apparel))
                        holder.apparel.Remove(apparel);
                    else
                        holder.inventory?.innerContainer.Remove(apparel);
                }
                pawn.apparel.Wear(apparel);
                apparel.def.apparel?.soundWear?.PlayOneShot(new TargetInfo(pawn.Position, map));
                return;
            }
            int count = Mathf.Min(thing.stackCount, MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
            if (count <= 0)
            {
                Messages.Message("RimWorldFPS: " + pawn.LabelShort + " can't carry any more.", pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (holder != null)
            {
                ThingOwner from = thing.holdingOwner;
                from?.TryTransferToContainer(thing, pawn.inventory.innerContainer, count);
            }
            else
                pawn.inventory.innerContainer.TryAdd(thing.SplitOff(count));
            thing.def.soundPickup?.PlayOneShot(new TargetInfo(pawn.Position, map));
        }

        /// <summary>The weapon in hand, apparel worn and things carried.</summary>
        private static IEnumerable<Thing> Kit(Pawn pawn)
        {
            if (pawn.equipment != null)
                foreach (ThingWithComps eq in pawn.equipment.AllEquipmentListForReading) yield return eq;
            if (pawn.apparel != null)
                foreach (Apparel apparel in pawn.apparel.WornApparel) yield return apparel;
            if (pawn.inventory != null)
                foreach (Thing thing in pawn.inventory.innerContainer) yield return thing;
        }

        /// <summary>The ground around the colonist, then each corpse or downed pawn there.</summary>
        private static IEnumerable<(string id, string label, List<Thing> things)> Sources(Pawn pawn)
        {
            Map map = pawn.Map;
            var ground = new List<Thing>();
            var bodies = new List<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, Reach, true))
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing is Corpse corpse && corpse.InnerPawn != null) bodies.Add(corpse.InnerPawn);
                    else if (thing is Pawn other && other != pawn && other.Downed) bodies.Add(other);
                    else if (thing.def.category == ThingCategory.Item && thing.def.EverHaulable && !(thing is Corpse))
                        ground.Add(thing);
                }
            }
            yield return ("ground", "Ground", ground);
            foreach (Pawn body in bodies)
            {
                Thing owner = body.Dead ? (Thing)body.Corpse : body;
                if (owner == null) continue;
                yield return (owner.ThingID, owner.LabelCap, Kit(body).ToList());
            }
        }

        /// <summary>The corpse's or downed pawn's own pawn, for a thing in its gear; null on the ground.</summary>
        private static Pawn Holder(Thing thing)
        {
            if (thing.Spawned) return null;
            return thing.ParentHolder switch
            {
                Pawn_EquipmentTracker eq => eq.pawn,
                Pawn_ApparelTracker apparel => apparel.pawn,
                Pawn_InventoryTracker inventory => inventory.pawn,
                _ => null,
            };
        }

        private static string ItemJson(Thing thing)
        {
            string kind = thing.def.IsWeapon ? "weapon" : thing is Apparel ? "apparel" : "item";
            return new Json()
                .Str("id", thing.ThingID)
                .Str("label", thing.LabelCap)
                .Str("def", thing.def.defName)
                .Num("count", (long)thing.stackCount)
                .Str("kind", kind)
                .Num("mass", thing.GetStatValue(StatDefOf.Mass) * thing.stackCount)
                .ToString();
        }
    }
}
