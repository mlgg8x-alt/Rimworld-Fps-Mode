using RimFps.Render;
using Raylib_cs;

namespace RimFps.Demo;

/// <summary>A tiny RGBA picture to paint the demo's stand-in RimWorld art on, top row first.</summary>
public sealed class Canvas
{
    public readonly int W, H;
    public readonly Color[] Px;

    public Canvas(int w, int h)
    {
        W = w;
        H = h;
        Px = new Color[w * h];
    }

    public void Set(int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        Px[y * W + x] = c;
    }

    public Color Get(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? default : Px[y * W + x];

    public void Fill(Color c) => Array.Fill(Px, c);

    public void Rect(int x, int y, int w, int h, Color c)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                Set(i, j, c);
    }

    public void Ellipse(float cx, float cy, float rx, float ry, Color c)
    {
        for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
            for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1f) Set(x, y, c);
            }
    }

    public void Line(float x0, float y0, float x1, float y1, float width, Color c)
    {
        float len = MathF.Max(1, MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        for (float t = 0; t <= 1; t += 0.5f / len)
            Ellipse(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, width / 2, width / 2, c);
    }

    /// <summary>A dark outline round everything solid, as RimWorld's art has.</summary>
    public void Outline(Color c, int thick = 2)
    {
        var solid = Px.Select(p => p.A > 0).ToArray();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (solid[y * W + x]) continue;
                bool near = false;
                for (int dy = -thick; dy <= thick && !near; dy++)
                    for (int dx = -thick; dx <= thick && !near; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < W && ny < H && solid[ny * W + nx] && dx * dx + dy * dy <= thick * thick + 1) near = true;
                    }
                if (near) Px[y * W + x] = c;
            }
    }

    /// <summary>Darkens or lightens what is painted by noise, for texture.</summary>
    public void Grain(float amount, int seed, int cells = 8)
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = Px[y * W + x];
                if (c.A == 0) continue;
                float n = PixelArt.Fbm(x, y, Math.Max(W, H), cells, seed) - 0.5f;
                Px[y * W + x] = PixelArt.Shade(c, 1f + n * amount);
            }
    }

    public Canvas Mirror()
    {
        var m = new Canvas(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                m.Px[y * W + x] = Px[y * W + (W - 1 - x)];
        return m;
    }

    public unsafe void Save(string path)
    {
        Image image = Raylib.GenImageColor(W, H, new Color(0, 0, 0, 0));
        Color* data = (Color*)image.Data;
        for (int i = 0; i < Px.Length; i++) data[i] = Px[i];
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
    }
}
