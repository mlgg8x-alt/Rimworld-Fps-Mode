using System.Numerics;
using RimFps.Net;
using RimFps.Render;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// RimWorld's bullets and shells, flying across the world. RimWorld decides when each is
/// fired and where it is aimed; a bullet aimed at the colonist the player plays flies at the
/// player's middle and on past, until something solid stops it, and only strikes if the
/// player is still there when it arrives: step aside, duck behind sandbags, or get hit, as
/// upstream's M8. All in RimWorld coordinates (x east, z north, y up, in cells).
/// </summary>
public sealed class Projectiles
{
    private const float FlyOn = 20f, Step = 0.05f, TracerLength = 0.9f, WhistleSeconds = 1.5f;

    private sealed class Tracer
    {
        public Vector3 From, To;
        public float Speed, Flown;
        public string Shooter = "";
        public string? Id;
        public bool Hit;
        public float Length => Vector3.Distance(From, To);
        public Vector3 Along(float d) => Vector3.Lerp(From, To, Math.Clamp(d / Math.Max(Length, 1e-3f), 0, 1));
    }

    private sealed class Flight
    {
        public Vector3 From, To;
        public float Seconds, Apex, Flown, Size = 0.4f, Fuse;
        public bool Arc, Whistled;
        public int Tex = -1;
        public Vector3 At(float t)
        {
            float f = Math.Clamp(t / Seconds, 0, 1);
            return Vector3.Lerp(From, To, f) + Vector3.UnitY * Apex * 4f * f * (1 - f);
        }
        public bool Landed => Flown >= Seconds;
    }

    private readonly List<Tracer> tracers = new();
    private readonly List<Flight> shells = new();

    /// <summary>Bullets struck the player this frame: bullet id, part, who fired it and from where.</summary>
    public readonly List<(string id, string part, string shooter, Vector3 from)> Struck = new();
    /// <summary>Mortar shells about to land, for their whistle.</summary>
    public readonly List<Vector3> Whistles = new();
    /// <summary>Bullets that just went close past the player's head, for their whizz.</summary>
    public int NearMisses;

    public int Count => tracers.Count + shells.Count;

    /// <summary>The player's body, as RimWorld's bullets see it.</summary>
    public readonly struct Body
    {
        public readonly Vector3 Min, Max;
        public readonly float Head;
        public Body(float x, float z, float eye, float feet = 0f)
        {
            float half = Dims.BodyHalfWidth;
            Min = new Vector3(x - half, feet, z - half);
            Max = new Vector3(x + half, eye + 0.14f, z + half);
            Head = eye - 0.1f;
        }
        public float Middle => (Min.Y + Max.Y) / 2f;

        /// <summary>How far along a to b (0 to 1) it first meets the body.</summary>
        public float? Entry(Vector3 a, Vector3 b)
        {
            float enter = 0, leave = 1;
            Vector3 way = b - a;
            for (int k = 0; k < 3; k++)
            {
                float ak = k == 0 ? a.X : k == 1 ? a.Y : a.Z, wk = k == 0 ? way.X : k == 1 ? way.Y : way.Z;
                float mn = k == 0 ? Min.X : k == 1 ? Min.Y : Min.Z, mx = k == 0 ? Max.X : k == 1 ? Max.Y : Max.Z;
                if (MathF.Abs(wk) < 1e-6f)
                {
                    if (ak < mn || ak > mx) return null;
                    continue;
                }
                float t0 = (mn - ak) / wk, t1 = (mx - ak) / wk;
                enter = MathF.Max(enter, MathF.Min(t0, t1));
                leave = MathF.Min(leave, MathF.Max(t0, t1));
                if (enter > leave) return null;
            }
            return enter;
        }
    }

    public void Add(Shot shot, Grid grid, Body? body, string? playing, Effects effects)
    {
        if (tracers.Count >= 300) return;
        float hs = Dims.ShotHeightScale;
        var from = new Vector3(shot.From[0], shot.From[1] * hs, shot.From[2]);
        var to = new Vector3(shot.To[0], shot.To[1] * hs, shot.To[2]);
        bool atPlayer = body.HasValue && shot.Hit && shot.Target != null && shot.Target == playing;
        if (atPlayer)
        {
            to.Y = body!.Value.Middle;
            Vector3 way = to - from;
            if (way.LengthSquared() > 1e-6f)
            {
                Vector3 far = from + Vector3.Normalize(way) * (way.Length() + FlyOn);
                to = FirstSolid(grid, from, far, cover: true) ?? far;
            }
        }
        else if (!shot.Hit)
        {
            // RimWorld only lands a hit along a clear line; a miss flies on until something stops it.
            to = FirstSolid(grid, from, to, cover: false) ?? to;
        }
        float speed = Math.Clamp(shot.Speed ?? 60f, 10f, 400f);
        tracers.Add(new Tracer { From = from, To = to, Speed = speed, Shooter = shot.Shooter, Id = shot.Id, Hit = shot.Hit });
        effects.Flash(from + Vector3.Normalize(to - from + new Vector3(1e-4f, 0, 0)) * 0.35f, 0.45f);
    }

    /// <summary>Where a bullet from a to b first meets something solid at its height; cells
    /// beside the shooter's are skipped, as RimWorld's shooters lean out past a corner.</summary>
    public static Vector3? FirstSolid(Grid grid, Vector3 a, Vector3 b, bool cover)
    {
        int sx = (int)MathF.Floor(a.X), sz = (int)MathF.Floor(a.Z);
        int steps = (int)MathF.Ceiling(Vector3.Distance(a, b) / Step);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
            int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Z);
            if (Math.Abs(cx - sx) <= 1 && Math.Abs(cz - sz) <= 1) continue;
            if (!grid.Inside(cx, cz)) return p;
            if (grid.Stops(cx, cz, p.Y, cover)) return p;
        }
        return null;
    }

    public void Add(Shell shell)
    {
        if (shells.Count >= 200) return;
        var from = new Vector3(shell.From[0], shell.From[1] * Dims.ShotHeightScale, shell.From[2]);
        var to = new Vector3(shell.To[0], 0.08f, shell.To[2]);
        float reach = Vector3.Distance(from, to);
        float apex = shell.Arc ? Math.Clamp(reach * 0.2f, 4f, 30f)
            : shell.Lob is float lob ? Math.Clamp(lob * 0.75f, 0.25f, 6f)
            : Math.Clamp(reach * 0.06f, 0.15f, 2f);
        shells.Add(new Flight
        {
            From = from,
            To = to,
            Seconds = Math.Clamp(shell.Seconds, 0.1f, 30f),
            Apex = apex,
            Arc = shell.Arc,
            Tex = shell.Tex ?? -1,
            Size = Math.Clamp(shell.Size ?? 0.5f, 0.15f, 2f),
            Fuse = Math.Clamp(shell.Fuse ?? 0f, 0f, 10f),
        });
    }

    /// <summary>An explosion RimWorld set off: the grenade lying there was the one going off.</summary>
    public void Exploded(Explosion e)
    {
        shells.RemoveAll(s => s.Fuse > 0 && s.Landed && MathF.Abs(s.To.X - e.X) + MathF.Abs(s.To.Z - e.Z) < 2.5f);
    }

    public void Update(float dt, Body? body, string? playing, Effects effects)
    {
        Struck.Clear();
        Whistles.Clear();
        foreach (Tracer t in tracers)
        {
            float before = t.Flown;
            t.Flown += t.Speed * dt;
            if (body.HasValue && t.Id != null && t.Shooter != playing && before < t.Length)
            {
                Vector3 a = t.Along(before), b = t.Along(t.Flown);
                if (body.Value.Entry(a, b) is float f)
                {
                    Vector3 at = Vector3.Lerp(a, b, f);
                    Struck.Add((t.Id, at.Y >= body.Value.Head ? "head" : "body", t.Shooter, t.From));
                    t.Id = null;
                    t.To = at;
                    t.Hit = true;
                    continue;
                }
                // A near miss past the player's head.
                Vector3 mid = (a + b) / 2;
                Vector3 centre = (body.Value.Min + body.Value.Max) / 2;
                if (Vector3.Distance(mid, centre) < 1.2f && t.Speed * dt > 0.01f) NearMisses++;
            }
            if (before < t.Length && t.Flown >= t.Length)
                effects.Puff(t.To, t.Hit ? Effects.Kind.Blood : Effects.Kind.Grit, 0.25f, 3);
        }
        tracers.RemoveAll(t => t.Flown - TracerLength >= t.Length);
        foreach (Flight s in shells)
        {
            s.Flown += dt;
            if (s.Arc && !s.Whistled && s.Seconds - s.Flown <= WhistleSeconds)
            {
                s.Whistled = true;
                Whistles.Add(s.To);
            }
        }
        shells.RemoveAll(s => s.Flown >= s.Seconds + s.Fuse + (s.Fuse > 0 ? 0.5f : 0f));
    }

    public void Draw(Sprites sprites, Builtins art, Pictures pictures, Vector3 eye, Func<float, float, float, Color> lit)
    {
        foreach (Tracer t in tracers)
        {
            if (t.Flown > t.Length + TracerLength) continue;
            Vector3 head = t.Along(t.Flown), tail = t.Along(t.Flown - TracerLength);
            if (Vector3.DistanceSquared(head, tail) < 1e-5f) continue;
            sprites.Streak(art.Tracer, WorldMesh.P(tail.X, tail.Y, tail.Z), WorldMesh.P(head.X, head.Y, head.Z), 0.05f, eye,
                new Color(255, 230, 150, 255));
        }
        foreach (Flight s in shells)
        {
            Vector3 at = s.Landed ? s.To : s.At(s.Flown);
            Vector3 world = WorldMesh.P(at.X, at.Y, at.Z);
            Pic? pic = s.Tex >= 0 ? pictures.Get(s.Tex) : null;
            if (pic != null)
            {
                float longest = Math.Max(pic.CutW, pic.CutH);
                float k = s.Size / Math.Max(longest, 1e-3f) * 0.8f;
                sprites.Spin(pic, world + new Vector3(0, 0.1f, 0), pic.CutW * k, pic.CutH * k, s.Flown * 540f, lit(at.X, at.Z, at.Y));
            }
            else
            {
                // A shell with no picture: a dark slug with a hot trail.
                sprites.Spin(art.Smoke, world, 0.25f, 0.25f, 0, new Color(40, 40, 40, 255), cut: false);
                if (!s.Landed)
                {
                    Vector3 back = s.At(s.Flown - 0.08f);
                    sprites.Streak(art.Tracer, WorldMesh.P(back.X, back.Y, back.Z), world, 0.12f, eye, new Color(255, 180, 90, 255));
                }
            }
        }
    }
}
