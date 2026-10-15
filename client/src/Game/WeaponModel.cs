using System.Numerics;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// A weapon to hold, as nothing but its RimWorld picture, drawn flat on the screen: every
/// weapon works, modded ones too, with no model to make. RimWorld draws some pictures
/// slanted (most melee weapons) and turns them straight by the def's equipped angle offset;
/// this does the same to find which way the weapon points, its length along that way, and,
/// from its outline, where the hands go (the grip hangs below the body in the rear half, the
/// barrel's underside takes the other hand) and where the muzzle is (the far end).
///
/// Points on the weapon are in its picture's own coordinates: 0 to 1 across the solid part,
/// x right and y down, as the picture is stored.
/// </summary>
public sealed class WeaponModel
{
    public Pic Pic = null!;
    public bool Melee;
    /// <summary>The solid part of the picture, in pixels.</summary>
    public int W, H;
    /// <summary>The way the weapon points in its picture, degrees clockwise from the right.</summary>
    public float Angle;
    /// <summary>Its length along that way, and its height across it, in RimWorld cells.</summary>
    public float Length, Height;
    /// <summary>Its length in the picture's pixels.</summary>
    public float LengthPixels;
    public Vector2 Grip, Fore, Muzzle;
    /// <summary>Two hands: the weapon is long enough to hold by its barrel too.</summary>
    public bool TwoHanded;

    private const int Detail = 96;

    /// <summary>The model of picture <paramref name="index"/>, drawn <paramref name="size"/>
    /// cells across in RimWorld and turned <paramref name="turn"/> degrees (the def's
    /// equipped angle offset) to point straight; null while the picture is not loaded.</summary>
    public static WeaponModel? Build(Pictures pictures, int index, Vector2 size, bool melee, float turn)
    {
        Pic? pic = pictures.Get(index);
        if (pic == null) return null;
        var read = pictures.Pixels(index, Detail);
        if (read == null) return null;
        var (pixels, w, h) = read.Value;
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && pixels[y * w + x].A >= 128;

        // Which way it points in its picture. Guns are drawn pointing right, as RimWorld has
        // them (turned by the def's offset, if any). Melee weapons are drawn every which way,
        // mostly slanted: their long axis, read off the outline, towards the right (or up).
        float angle = melee ? LongAxis(Solid, w, h) : -turn;
        float a = angle * MathF.PI / 180f;
        var along = new Vector2(MathF.Cos(a), MathF.Sin(a));
        var across = new Vector2(-along.Y, along.X); // the weapon's underside
        var centre = new Vector2(w / 2f, h / 2f);
        float sMin = float.MaxValue, sMax = float.MinValue, tMin = float.MaxValue, tMax = float.MinValue;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Solid(x, y)) continue;
                var q = new Vector2(x + 0.5f, y + 0.5f) - centre;
                float s = Vector2.Dot(q, along), t = Vector2.Dot(q, across);
                sMin = Math.Min(sMin, s - 0.5f);
                sMax = Math.Max(sMax, s + 0.5f);
                tMin = Math.Min(tMin, t - 0.5f);
                tMax = Math.Max(tMax, t + 0.5f);
            }
        if (sMax <= sMin) return null;
        float perCell = w / Math.Max(0.05f, size.X * pic.CutW);
        var model = new WeaponModel
        {
            Pic = pic,
            Melee = melee,
            W = w,
            H = h,
            Angle = angle,
            LengthPixels = sMax - sMin,
            Length = (sMax - sMin) / perCell,
            Height = (tMax - tMin) / perCell,
        };
        // The weapon straightened: (x, y) along it from the back and across it from the top.
        int sw = Math.Max(1, (int)MathF.Ceiling(sMax - sMin)), sh = Math.Max(1, (int)MathF.Ceiling(tMax - tMin));
        Vector2 Back(float x, float y) => centre + along * (sMin + x) + across * (tMin + y);
        bool Straight(int x, int y)
        {
            if (x < 0 || y < 0 || x >= sw || y >= sh) return false;
            Vector2 q = Back(x + 0.5f, y + 0.5f);
            return Solid((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y));
        }
        Vector2 ToPicture(float x, float y)
        {
            Vector2 q = Back(x, y);
            return new Vector2(q.X / w, q.Y / h);
        }
        model.FindHands(Straight, sw, sh, ToPicture);
        return model;
    }

    /// <summary>The picture's long axis, degrees clockwise from the right, pointing right or up.</summary>
    private static float LongAxis(Func<int, int, bool> solid, int w, int h)
    {
        double n = 0, mx = 0, my = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (solid(x, y))
                {
                    n++;
                    mx += x;
                    my += y;
                }
        if (n < 2) return 0;
        mx /= n;
        my /= n;
        double xx = 0, yy = 0, xy = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (solid(x, y))
                {
                    xx += (x - mx) * (x - mx);
                    yy += (y - my) * (y - my);
                    xy += (x - mx) * (y - my);
                }
        float angle = (float)(0.5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI);
        // Towards the right; straight up and down, upwards.
        float dx = MathF.Cos(angle * MathF.PI / 180f), dy = MathF.Sin(angle * MathF.PI / 180f);
        if (dx < -0.2f || MathF.Abs(dx) <= 0.2f && dy > 0) angle += 180f;
        return angle;
    }

    /// <summary>Where the hands and the muzzle are, from the outline (straightened).</summary>
    private void FindHands(Func<int, int, bool> solid, int w, int h, Func<float, float, Vector2> at)
    {
        var topRow = new int[w];
        var bottomRow = new int[w];
        for (int x = 0; x < w; x++)
        {
            topRow[x] = -1;
            bottomRow[x] = -1;
            for (int y = 0; y < h; y++)
                if (solid(x, y))
                {
                    if (topRow[x] < 0) topRow[x] = y;
                    bottomRow[x] = y;
                }
        }
        float Middle(int x) => topRow[x] < 0 ? h / 2f : (topRow[x] + bottomRow[x]) / 2f;
        int last = Array.FindLastIndex(bottomRow, b => b >= 0);
        if (last < 0) last = w - 1;
        if (Melee)
        {
            // A blade's handle is its back end: hold it a little way in.
            int hx = Math.Clamp((int)(w * 0.1f), 0, w - 1);
            Grip = at(hx + 0.5f, Middle(hx));
            int fx = Math.Clamp((int)(w * 0.24f), 0, w - 1);
            Fore = at(fx + 0.5f, Middle(fx));
            Muzzle = at(last + 1, Middle(last));
            TwoHanded = false;
            return;
        }
        // The body's underside, as most columns have it; the grip hangs below it in the rear half.
        var under = Enumerable.Range((int)(w * 0.2f), Math.Max(1, (int)(w * 0.7f)))
            .Where(x => x < w && bottomRow[x] >= 0).Select(x => bottomRow[x]).OrderBy(v => v).ToList();
        float body = under.Count > 0 ? under[under.Count / 2] : h * 0.6f;
        int gripX = (int)(w * 0.35f);
        float deepest = body + 1.5f;
        for (int x = (int)(w * 0.12f); x < (int)(w * 0.62f); x++)
            if (bottomRow[x] > deepest)
            {
                deepest = bottomRow[x];
                gripX = x;
            }
        float gripY = deepest > body + 1.5f ? (body + deepest) / 2f + 1 : body;
        Grip = at(gripX + 0.5f, gripY);
        int foreX = Math.Clamp(Math.Max(gripX + (int)(w * 0.25f), (int)(w * 0.6f)), 0, Math.Max(0, last - 2));
        Fore = at(foreX + 0.5f, bottomRow[foreX] >= 0 ? bottomRow[foreX] : body);
        Muzzle = at(last + 1, Middle(last));
        TwoHanded = Length > 0.7f;
    }
}
