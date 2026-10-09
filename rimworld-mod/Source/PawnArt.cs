using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M10: colonists and raiders as RimWorld draws them. RimWorld renders each person facing
    /// north, east and south the way it draws them on the map, clothes, hair and all; those
    /// pictures go to IW4L with the pawn (<c>art</c> and <c>draw</c>, as animals' already do),
    /// which stands them up as cards instead of MW2 soldiers. A person is drawn again only
    /// when their look changes, so the pictures are saved once each.
    /// </summary>
    public static class PawnArt
    {
        /// <summary>
        /// How many cells a person's picture spans: RimWorld's portrait camera is orthographic
        /// with size 1 at zoom 1 (PawnCacheRenderer), so the picture is 2 cells a side.
        /// </summary>
        private const float Cells = 2f;

        /// <summary>How many cells a person's picture spans.</summary>
        public static float Span => Cells;

        /// <summary>A person's picture facing south (M15: one being carried); -1 when RimWorld
        /// could not draw them yet.</summary>
        public static int South(Pawn pawn) => Of(pawn)?[2] ?? -1;
        /// <summary>Pictures kept for at most this many people at once; the longest unseen are
        /// forgotten first.</summary>
        private const int MostPeople = 400;

        /// <summary>New people drawn in one snapshot at most: a raid's arrive over a few
        /// snapshots rather than in one long stall (M15). M22: 8 left a big raid half drawn
        /// for seconds, shown meanwhile as MW2 soldiers and boxes; now nobody is shown before
        /// their picture, so it comes sooner.</summary>
        private const int NewPerSnapshot = 24;
        /// <summary>Seconds before trying again a person RimWorld could not draw.</summary>
        private const float RetrySeconds = 2f;

        private sealed class Drawn
        {
            public string look;
            public int[] art;
            public float tried;
            public int seen;
        }

        private static int drawnThisFrame;
        private static int budgetFrame = -1;

        private static readonly Dictionary<string, Drawn> drawn = new Dictionary<string, Drawn>();

        public static void Reset() => drawn.Clear();

        /// <summary>
        /// A person's own pictures on <paramref name="json"/>, as RimWorld draws them: facing
        /// north, east and south, cut out, and how many cells they span. False when there are
        /// none yet: not drawn yet (IW4L shows nobody until then), or RimWorld could not draw
        /// their portrait (<see cref="CouldNotDraw"/>).
        /// </summary>
        public static bool Art(Pawn pawn, Json json)
        {
            int[] art = Of(pawn);
            if (art == null) return false;
            json.Raw("art", "[" + art[0] + "," + art[1] + "," + art[2] + "]")
                .Raw("draw", "[" + F(Cells) + "," + F(Cells) + "]");
            return true;
        }

        /// <summary>Whether RimWorld was asked for this person's portrait and could not draw it
        /// (some mods' people): their body as RimWorld draws it on the map stands in (M22).</summary>
        public static bool CouldNotDraw(Pawn pawn) =>
            drawn.TryGetValue(pawn.ThingID, out Drawn had) && had.art == null;

        /// <summary>The three pictures of <paramref name="pawn"/>, drawn now if their look
        /// changed since last time; null when RimWorld could not draw them.</summary>
        private static int[] Of(Pawn pawn)
        {
            string look = Look(pawn);
            float now = Time.realtimeSinceStartup;
            if (drawn.TryGetValue(pawn.ThingID, out Drawn had))
            {
                had.seen = Time.frameCount;
                // M15: a person RimWorld could not draw is tried again, not left a box for good.
                if (had.look == look && (had.art != null || now - had.tried < RetrySeconds)) return had.art;
            }
            if (budgetFrame != Time.frameCount)
            {
                budgetFrame = Time.frameCount;
                drawnThisFrame = 0;
            }
            if (drawnThisFrame >= NewPerSnapshot) return had?.art;
            if (drawn.Count >= MostPeople && had == null) Forget();
            if (drawn.Count >= MostPeople && had == null) return null;
            drawnThisFrame++;
            int[] art = Draw(pawn, look);
            drawn[pawn.ThingID] = new Drawn { look = look, art = art ?? had?.art, tried = now, seen = Time.frameCount };
            if (art == null && had?.art == null) drawn[pawn.ThingID].look = look;
            return art ?? had?.art;
        }

        /// <summary>
        /// Forgets the people not seen for a while: the ones gone (a raid that left, the dead
        /// carried off). M15: they were kept for good, so after a few raids nobody new was drawn.
        /// </summary>
        private static void Forget()
        {
            int before = Time.frameCount - 600;
            var gone = new List<string>();
            foreach (KeyValuePair<string, Drawn> pair in drawn)
                if (pair.Value.seen < before) gone.Add(pair.Key);
            foreach (string id in gone) drawn.Remove(id);
        }

        private static int[] Draw(Pawn pawn, string look)
        {
            try
            {
                var size = new Vector2(TextureExport.Size, TextureExport.Size);
                int north = Picture(pawn, size, Rot4.North, look);
                int east = Picture(pawn, size, Rot4.East, look);
                int south = Picture(pawn, size, Rot4.South, look);
                return north < 0 || east < 0 || south < 0 ? null : new[] { north, east, south };
            }
            catch
            {
                // Someone RimWorld draws its own way (a mod): their MW2 soldier stays.
                return null;
            }
        }

        private static int Picture(Pawn pawn, Vector2 size, Rot4 facing, string look)
        {
            RenderTexture picture = PortraitsCache.Get(pawn, size, facing, default, 1f,
                supersample: true, compensateForUIScale: false, renderHeadgear: true, renderClothes: true);
            return picture == null ? -1 : TextureExport.OfDrawn(picture, "pawn_" + Key(look) + "_" + facing.AsInt);
        }

        /// <summary>Everything about a person that changes how RimWorld draws them.</summary>
        private static string Look(Pawn pawn)
        {
            var sb = new StringBuilder(pawn.def?.defName);
            sb.Append('|').Append(pawn.story?.bodyType?.defName).Append('|').Append(pawn.story?.headType?.defName)
                .Append('|').Append(pawn.story?.hairDef?.defName).Append('|').Append(pawn.gender)
                .Append('|').Append(Rgb(pawn.story == null ? Color.white : pawn.story.SkinColor))
                .Append('|').Append(Rgb(pawn.story == null ? Color.white : pawn.story.HairColor))
                .Append('|').Append(pawn.ageTracker?.CurLifeStage?.defName)
                .Append('|').Append(pawn.Dead ? 'd' : pawn.Downed ? 'x' : '-');
            var worn = pawn.apparel?.WornApparel;
            if (worn != null)
                foreach (Apparel apparel in worn)
                    sb.Append('|').Append(apparel.def?.defName).Append(':').Append(Rgb(apparel.DrawColor));
            return sb.ToString();
        }

        /// <summary>A short file name for a look, the same for two people who look alike.</summary>
        private static string Key(string look)
        {
            uint hash = 2166136261;
            foreach (char c in look)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        private static string Rgb(Color c) =>
            ((int)(c.r * 255f)).ToString(CultureInfo.InvariantCulture) + ","
            + ((int)(c.g * 255f)).ToString(CultureInfo.InvariantCulture) + ","
            + ((int)(c.b * 255f)).ToString(CultureInfo.InvariantCulture);

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
