using System.Numerics;
using RimFps.Net;
using RimFps.Render;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>A pawn as the client draws it: where it is, smoothed, which way it faces, and
/// how it looks (RimWorld's own pictures of it).</summary>
public sealed class Figure
{
    public string Id = "";
    public PawnState Pawn = new();
    public float X, Z;
    /// <summary>Degrees clockwise from north, turned smoothly.</summary>
    public float Yaw;
    public float Vx, Vz;
    public int[]? Art;
    public float[]? Draw;
    /// <summary>The pictures it had before, shown while new ones load.</summary>
    public int[]? WasArt;
    public float[]? WasDraw;
    public int Hurt, Swings;
    public float Flash, Swing;
    /// <summary>How it was drawn last frame, for bullets to find it.</summary>
    public float Width, Height, Lift;
    public bool Shown;

    public bool Lying => Pawn.Downed || Pawn.Lie.HasValue || Pawn.Dead;
}

/// <summary>Something a bullet can strike: a pawn's box, and how its height divides into
/// the parts RimWorld is told a bullet struck.</summary>
public readonly record struct Target(string Id, float X, float Z, float Half, float Bottom, float Top, bool Parts, bool Lying);

/// <summary>
/// Every pawn on the map in view, as RimWorld's own pictures standing in the world and turning
/// to face you, the way Doom's monsters are sprites: their front seen from the front, their
/// back from behind, their side (mirrored for the other side) in between.
/// </summary>
public sealed class Figures
{
    private const float Follow = 12f, Jump = 4f, Smooth = 8f, TurnRate = 9f, FaceWalkFrom = 0.25f;
    private const float HurtSeconds = 0.35f, SwingSeconds = 0.4f, Lunge = 0.5f, Hands = 0.42f;
    private const float SwingRaised = 80f, SwingStruck = -60f, BedTop = 0.45f;

    public readonly Dictionary<string, Figure> All = new();
    public readonly List<Target> Targets = new();
    /// <summary>How much people's pictures grow to stand <see cref="Dims.PersonHeight"/> tall:
    /// animals and mechs grow as much, so they keep RimWorld's sizes against people.</summary>
    public float PeopleScale = 0.55f;

    public void Update(Snapshot? snap, float dt)
    {
        if (snap == null) return;
        var seen = new HashSet<string>();
        float step = 1f - MathF.Exp(-Follow * dt);
        float smooth = 1f - MathF.Exp(-Smooth * dt);
        float turn = 1f - MathF.Exp(-TurnRate * dt);
        foreach (PawnState pawn in snap.Pawns)
        {
            seen.Add(pawn.Id);
            if (!All.TryGetValue(pawn.Id, out Figure? f) || MathF.Abs(f.X - pawn.X) + MathF.Abs(f.Z - pawn.Z) > Jump)
            {
                All[pawn.Id] = new Figure
                {
                    Id = pawn.Id,
                    Pawn = pawn,
                    X = pawn.X,
                    Z = pawn.Z,
                    Yaw = pawn.Rot,
                    Art = pawn.Art,
                    Draw = pawn.Draw,
                    Hurt = pawn.Hurt,
                    Swings = pawn.Swings,
                };
                continue;
            }
            float nx = f.X + (pawn.X - f.X) * step, nz = f.Z + (pawn.Z - f.Z) * step;
            float vx = dt > 0 ? (nx - f.X) / dt : 0, vz = dt > 0 ? (nz - f.Z) / dt : 0;
            f.Vx += (vx - f.Vx) * smooth;
            f.Vz += (vz - f.Vz) * smooth;
            f.X = nx;
            f.Z = nz;
            bool walking = !pawn.Downed && MathF.Sqrt(f.Vx * f.Vx + f.Vz * f.Vz) > FaceWalkFrom;
            float want = walking ? Heading(f.Vx, f.Vz) : pawn.Rot;
            f.Yaw = TurnTowards(f.Yaw, want, turn);
            f.Flash = pawn.Hurt > f.Hurt ? HurtSeconds : Math.Max(0, f.Flash - dt);
            f.Swing = pawn.Swings > f.Swings ? SwingSeconds : Math.Max(0, f.Swing - dt);
            f.Hurt = pawn.Hurt;
            f.Swings = pawn.Swings;
            // A snapshot without pictures keeps the ones it had; new ones keep the old to
            // show until they have loaded.
            int[]? art = pawn.Art ?? f.Art;
            float[]? draw = pawn.Draw ?? f.Draw;
            if (f.Art != null && art != null && !art.SequenceEqual(f.Art))
            {
                f.WasArt = f.Art;
                f.WasDraw = f.Draw;
            }
            f.Art = art;
            f.Draw = draw;
            f.Pawn = pawn;
        }
        foreach (string id in All.Keys.Where(id => !seen.Contains(id)).ToList()) All.Remove(id);
    }

    /// <summary>Degrees clockwise from north of something moving vx east and vz north.</summary>
    public static float Heading(float vx, float vz) => (MathF.Atan2(vx, vz) * 180f / MathF.PI + 360f) % 360f;

    /// <summary>The shortest turn from one angle to another, in degrees (-180 to 180).</summary>
    public static float AngleDiff(float from, float to) => ((to - from) % 360f + 540f) % 360f - 180f;

    public static float TurnTowards(float from, float to, float share)
    {
        float delta = ((to - from + 540f) % 360f) - 180f;
        return (from + delta * share + 360f) % 360f;
    }

    /// <summary>The picture to show of <paramref name="f"/> seen from (ex, ez): its index,
    /// mirrored or not; null while it has none loaded.</summary>
    private static (Pic pic, bool flip, float[] draw)? Card(Figure f, float ex, float ez, Pictures pictures)
    {
        (Pic, bool, float[])? Of(int[]? art, float[]? draw)
        {
            if (art == null || draw == null) return null;
            float around = (Heading(ex - f.X, ez - f.Z) - f.Yaw + 360f) % 360f;
            int index;
            bool flip = false;
            if (f.Lying && f.Pawn.Lie.HasValue) index = art[2];
            else if (around < 45f || around >= 315f) index = art[2];
            else if (around >= 135f && around < 225f) index = art[0];
            else
            {
                index = art[1];
                flip = around >= 180f;
            }
            Pic? pic = pictures.Get(index);
            return pic == null ? null : (pic, flip, draw);
        }
        return Of(f.Art, f.Draw) ?? Of(f.WasArt, f.WasDraw);
    }

    /// <summary>The middle of what each standing person needs to stand as tall as a person.</summary>
    private void MeasurePeople(Pictures pictures)
    {
        var needs = new List<float>();
        foreach (Figure f in All.Values)
        {
            if (!f.Pawn.Humanlike || f.Lying || f.Art == null || f.Draw == null) continue;
            Pic? pic = pictures.Get(f.Art[1]);
            if (pic == null) continue;
            float h = f.Draw[1] * pic.CutH;
            if (h > 1e-3f) needs.Add(Dims.PersonHeight / h);
        }
        if (needs.Count == 0) return;
        needs.Sort();
        PeopleScale = needs[needs.Count / 2];
    }

    /// <summary>Puts every pawn but the one played into <paramref name="sprites"/>, lit where it
    /// stands, and keeps what bullets can strike in <see cref="Targets"/>.</summary>
    public void Draw(Sprites sprites, Pictures pictures, Grid grid, Vector3 eye, string? playing, float sky, Func<float, float, float, Color> lit)
    {
        MeasurePeople(pictures);
        Targets.Clear();
        float ex = eye.X, ez = -eye.Z;
        foreach (Figure f in All.Values)
        {
            f.Shown = false;
            if (f.Id == playing) continue;
            var card = Card(f, ex, ez, pictures);
            if (card == null) continue;
            var (pic, flip, draw) = card.Value;
            float w = draw[0] * pic.CutW, h = draw[1] * pic.CutH;
            float scale = f.Pawn.Humanlike && h > 1e-3f ? Dims.PersonHeight / h : PeopleScale;
            w *= scale;
            h *= scale;
            float lift = f.Pawn.Bed ? BedTop : 0f;
            // A melee lunge carries the body at its target.
            float lunge = SwingLunge(f.Swing) * Lunge;
            float aim = AimOf(f);
            float px = f.X + MathF.Sin(aim * MathF.PI / 180f) * lunge, pz = f.Z + MathF.Cos(aim * MathF.PI / 180f) * lunge;
            Color color = lit(px, pz, lift + h * 0.6f);
            if (f.Flash > 0)
            {
                float k = f.Flash / HurtSeconds * 0.75f;
                color = new Color(color.R, (byte)(color.G * (1 - k)), (byte)(color.B * (1 - k)), color.A);
            }
            Vector3 foot = WorldMesh.P(px, lift, pz);
            if (f.Lying)
            {
                // Lying: the picture on its side, as Doom draws its dead, head the way
                // RimWorld turns the body (left or right of the view).
                float head = f.Pawn.Lie ?? f.Yaw + 90f;
                Vector3 right = sprites.Right;
                Vector3 headDir = new(MathF.Sin(head * MathF.PI / 180f), 0, -MathF.Cos(head * MathF.PI / 180f));
                bool headRight = Vector3.Dot(headDir, right) >= 0;
                sprites.Spin(pic, foot + new Vector3(0, w / 2f * 0.9f, 0), w, h, headRight ? 90f : -90f, color, flip);
                f.Width = h;
                f.Height = w * 0.9f;
            }
            else
            {
                sprites.Stand(pic, foot, w, h, color, flip);
                f.Width = w;
                f.Height = h;
                if (f.Pawn.Held != null) Held(sprites, pictures, f, px, pz, lift, h, scale, eye, color);
                if (f.Pawn.Carry != null) Carried(sprites, pictures, f, px, pz, lift, h, color);
            }
            f.Lift = lift;
            f.Shown = true;
            if (!f.Pawn.Dead)
                Targets.Add(new Target(f.Id, f.X, f.Z, MathF.Max(0.18f, f.Width * 0.35f), lift, lift + f.Height,
                    f.Pawn.Humanlike && !f.Lying, f.Lying));
        }
    }

    /// <summary>Where a pawn points what it holds, degrees clockwise from north: where RimWorld
    /// says it aims, else forward and to its right, as RimWorld carries a weapon.</summary>
    private static float AimOf(Figure f) => f.Pawn.Held?.Aim ?? f.Yaw + 53f;

    private static float SwingLunge(float left) =>
        left <= 0 ? 0 : MathF.Max(0, MathF.Sin(MathF.PI * (1 - left / SwingSeconds)));

    private static float SwingAngle(float left)
    {
        if (left <= 0) return 0;
        float t = Math.Clamp(1 - left / SwingSeconds, 0, 1);
        float eased = t * t * (3 - 2 * t);
        return SwingRaised + (SwingStruck - SwingRaised) * eased;
    }

    /// <summary>
    /// The weapon in a pawn's hands: RimWorld's picture of it at the hands, pointing where the
    /// pawn aims as that looks from the eye (towards the eye points it down, away from it up),
    /// mirrored to point left as RimWorld mirrors it to point west. In a melee swing it comes
    /// down from raised to struck.
    /// </summary>
    private void Held(Sprites sprites, Pictures pictures, Figure f, float x, float z, float lift, float h, float scale, Vector3 eye, Color color)
    {
        Held held = f.Pawn.Held!;
        Pic? pic = pictures.Get(held.Tex);
        if (pic == null) return;
        Vector3 at = WorldMesh.P(x, 0, z);
        Vector3 toEye = new Vector3(eye.X - at.X, 0, eye.Z - at.Z);
        toEye = toEye.LengthSquared() > 1e-6f ? Vector3.Normalize(toEye) : Vector3.UnitZ;
        Vector3 right = sprites.Right;
        float aimDeg = AimOf(f);
        Vector3 aim = new(MathF.Sin(aimDeg * MathF.PI / 180f), 0, -MathF.Cos(aimDeg * MathF.PI / 180f));
        float toward = Vector3.Dot(aim, toEye);
        float rest = held.Aim.HasValue ? 0f : 0.35f;
        var seen = new Vector2(Vector3.Dot(aim, right), -0.45f * toward - rest);
        seen = seen.LengthSquared() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(seen);
        float angle = (MathF.Atan2(seen.X, seen.Y) * 180f / MathF.PI + 360f) % 360f;
        float lifted = SwingAngle(f.Swing);
        angle = ((angle < 180f ? angle - lifted : angle + lifted) + 360f) % 360f;
        bool flip = angle > 200f && angle < 340f;
        float spin = flip ? angle - 270f - held.Turn : angle - 90f + held.Turn;
        float perCell = scale;
        float w = held.W * perCell * pic.CutW, hh = held.H * perCell * pic.CutH;
        float reach = Math.Max(0, 0.4f + held.Reach) * perCell;
        float front = toward < -0.3f ? -0.04f : 0.04f;
        Vector3 centre = at + new Vector3(0, lift + h * Hands, 0) + aim * reach + toEye * front;
        sprites.Spin(pic, centre, w, hh, spin, color, flip);
    }

    /// <summary>What a pawn carries: someone lying across its arms, a thing held in front.</summary>
    private void Carried(Sprites sprites, Pictures pictures, Figure f, float x, float z, float lift, float h, Color color)
    {
        Carried carry = f.Pawn.Carry!;
        Pic? pic = pictures.Get(carry.Tex);
        if (pic == null) return;
        float facing = f.Yaw * MathF.PI / 180f;
        Vector3 at = WorldMesh.P(x + MathF.Sin(facing) * 0.3f, lift + h * 0.42f, z + MathF.Cos(facing) * 0.3f);
        float scale = carry.Size * PeopleScale;
        if (carry.Pawn)
            sprites.Spin(pic, at, pic.CutW * scale, pic.CutH * scale, 90f, color);
        else
        {
            float longest = Math.Max(pic.CutW, pic.CutH);
            float s = Math.Min(carry.Size, 0.6f) / Math.Max(longest, 1e-3f) * 0.8f;
            sprites.Spin(pic, at, pic.CutW * s, pic.CutH * s, 0f, color);
        }
    }
}
