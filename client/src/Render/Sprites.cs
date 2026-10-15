using System.Numerics;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Render;

/// <summary>
/// Everything drawn as flat pictures in the world each frame: pawns, plants, items, effects,
/// bullets. Collected as quads, then drawn in as few batches as the atlas pages allow (one
/// texture each), lit ones first, then the fullbright ones (fire, flashes, tracers). They
/// need no sorting: the shader keeps a pixel or drops it, never blends.
/// </summary>
public sealed class Sprites
{
    private struct Quad
    {
        public int Page;
        public bool Bright;
        public Vector3 A, B, C, D;
        public Vector2 TA, TB, TC, TD;
        public Color Color;
    }

    private readonly List<Quad> quads = new();

    /// <summary>The camera's right, along the ground, for sprites to face it.</summary>
    public Vector3 Right = Vector3.UnitX;

    public int Count => quads.Count;

    /// <summary>A quad a, b, c, d (bottom left, bottom right, top right, top left) showing
    /// (u0, v0)..(u1, v1) of the picture.</summary>
    public void Add(Region r, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float v0, float u1, float v1, Color color, bool bright = false)
    {
        quads.Add(new Quad
        {
            Page = r.Page,
            Bright = bright,
            A = a,
            B = b,
            C = c,
            D = d,
            TA = r.At(u0, v1),
            TB = r.At(u1, v1),
            TC = r.At(u1, v0),
            TD = r.At(u0, v0),
            Color = color,
        });
    }

    /// <summary>
    /// A picture standing on <paramref name="foot"/> (world space), turned to face the camera
    /// as Doom's sprites do, <paramref name="w"/> by <paramref name="h"/>, showing only the
    /// picture's solid part; mirrored with <paramref name="flip"/>.
    /// </summary>
    public void Stand(Pic pic, Vector3 foot, float w, float h, Color color, bool flip = false, bool bright = false)
    {
        Vector3 half = Right * (w / 2f);
        Vector3 up = new(0, h, 0);
        float u0 = pic.Cut[0], u1 = pic.Cut[2], v0 = pic.Cut[1], v1 = pic.Cut[3];
        if (flip) (u0, u1) = (u1, u0);
        Add(pic.R, foot - half, foot + half, foot + half + up, foot - half + up, u0, v0, u1, v1, color, bright);
    }

    /// <summary>A picture facing the camera, turned <paramref name="degrees"/> clockwise as
    /// seen, its middle at <paramref name="centre"/>.</summary>
    public void Spin(Pic pic, Vector3 centre, float w, float h, float degrees, Color color, bool flip = false, bool bright = false, bool cut = true)
    {
        float r = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(r), sin = MathF.Sin(r);
        // Clockwise as seen: the picture's right goes down, its top goes right.
        Vector3 across = Right * cos - Vector3.UnitY * sin;
        Vector3 up = Right * sin + Vector3.UnitY * cos;
        Vector3 hw = across * (w / 2f), hh = up * (h / 2f);
        float u0 = cut ? pic.Cut[0] : 0, u1 = cut ? pic.Cut[2] : 1, v0 = cut ? pic.Cut[1] : 0, v1 = cut ? pic.Cut[3] : 1;
        if (flip) (u0, u1) = (u1, u0);
        Add(pic.R, centre - hw - hh, centre + hw - hh, centre + hw + hh, centre - hw + hh, u0, v0, u1, v1, color, bright);
    }

    /// <summary>A picture lying flat on the ground (a puddle, a splotch), its middle at
    /// <paramref name="centre"/>, turned <paramref name="degrees"/> clockwise from north.</summary>
    public void Flat(Pic pic, Vector3 centre, float w, float h, float degrees, Color color, bool bright = false, bool cut = false)
    {
        float r = degrees * MathF.PI / 180f;
        // The picture's top points to `degrees` from north; north is -Z in the world.
        Vector3 top = new(MathF.Sin(r), 0, -MathF.Cos(r));
        Vector3 right = new(-top.Z, 0, top.X);
        Vector3 hw = right * (w / 2f), hh = top * (h / 2f);
        float u0 = cut ? pic.Cut[0] : 0, u1 = cut ? pic.Cut[2] : 1, v0 = cut ? pic.Cut[1] : 0, v1 = cut ? pic.Cut[3] : 1;
        Add(pic.R, centre - hw - hh, centre + hw - hh, centre + hw + hh, centre - hw + hh, u0, v0, u1, v1, color, bright);
    }

    /// <summary>A streak from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/>
    /// across, turned to face the eye: tracers, rain.</summary>
    public void Streak(Pic pic, Vector3 a, Vector3 b, float width, Vector3 eye, Color color, bool bright = true)
    {
        Vector3 along = b - a;
        if (along.LengthSquared() < 1e-8f) return;
        Vector3 toEye = eye - (a + b) / 2f;
        Vector3 side = Vector3.Cross(along, toEye);
        if (side.LengthSquared() < 1e-8f) side = Vector3.Cross(along, Vector3.UnitY);
        if (side.LengthSquared() < 1e-8f) return;
        side = Vector3.Normalize(side) * (width / 2f);
        Add(pic.R, a - side, a + side, b + side, b - side, 0, 0, 1, 1, color, bright);
    }

    /// <summary>Draws and forgets everything collected, with the sprite shader.</summary>
    public void Flush(Renderer retro, Atlas atlas)
    {
        if (quads.Count == 0) return;
        quads.Sort((x, y) => x.Bright != y.Bright ? x.Bright.CompareTo(y.Bright) : x.Page.CompareTo(y.Page));
        Raylib.BeginShaderMode(retro.SpriteShader);
        bool bright = false;
        retro.Fullbright(false);
        int page = -1;
        foreach (Quad q in quads)
        {
            if (q.Bright != bright || q.Page != page)
            {
                if (page >= 0)
                {
                    Rlgl.End();
                    Rlgl.DrawRenderBatchActive();
                }
                if (q.Bright != bright)
                {
                    bright = q.Bright;
                    retro.Fullbright(bright);
                }
                page = q.Page;
                Rlgl.SetTexture(atlas.Texture(page).Id);
                Rlgl.Begin((int)DrawMode.Quads);
            }
            Rlgl.CheckRenderBatchLimit(4);
            Vertex(q.A, q.TA, q.Color);
            Vertex(q.B, q.TB, q.Color);
            Vertex(q.C, q.TC, q.Color);
            Vertex(q.D, q.TD, q.Color);
        }
        Rlgl.End();
        Rlgl.DrawRenderBatchActive();
        Rlgl.SetTexture(0);
        retro.Fullbright(false);
        Raylib.EndShaderMode();
        quads.Clear();
    }

    private static void Vertex(Vector3 p, Vector2 t, Color c)
    {
        Rlgl.Color4ub(c.R, c.G, c.B, c.A);
        Rlgl.TexCoord2f(t.X, t.Y);
        Rlgl.Vertex3f(p.X, p.Y, p.Z);
    }
}
