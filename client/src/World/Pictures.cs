using System.Diagnostics;
using RimFps.Net;
using Raylib_cs;

namespace RimFps.World;

/// <summary>One picture, ready to draw: where it is in the atlas, the part of it that is
/// not see-through (0 to 1, left, top, right, bottom), and its average colour.</summary>
public sealed class Pic
{
    public Region R;
    public float[] Cut = { 0, 0, 1, 1 };
    public Color Average = new(128, 128, 128, 255);
    /// <summary>Drawn cut out: see-through where RimWorld's picture is.</summary>
    public bool Cutout;
    /// <summary>For a linked wall's top: its front face, to put up the wall's sides.</summary>
    public Pic? Face;
    /// <summary>Cells it spans before it repeats, laid on the ground: RimWorld's ground art
    /// spans 16.</summary>
    public int Span = 1;

    public float CutW => Cut[2] - Cut[0];
    public float CutH => Cut[3] - Cut[1];
}

/// <summary>
/// RimWorld's own art, which the mod saves as PNG files (the <c>textures</c> message names
/// them), made chunky for the boomer look: shrunk with a box filter to a few dozen pixels a
/// cell, its edges made hard (see-through or not, nothing between) and put in the atlas.
/// Pictures load lazily, a few milliseconds' worth a frame, so a raid arriving never stalls.
/// </summary>
public sealed class Pictures
{
    private readonly Atlas atlas;
    private readonly Settings settings;
    private string dir = "";
    private List<string> names = new();
    private Pic?[] pics = Array.Empty<Pic?>();
    private byte[] state = Array.Empty<byte>(); // 0 not asked, 1 queued, 2 ready, 3 failed
    private readonly Queue<int> queue = new();
    private int textureRev = -1;

    /// <summary>Goes up whenever pictures load, so anything that drew a stand-in can redraw.</summary>
    public int Revision { get; private set; }
    public int Count => names.Count;
    public int Waiting => queue.Count;

    public Pictures(Atlas atlas, Settings settings)
    {
        this.atlas = atlas;
        this.settings = settings;
    }

    public void Sync(Link link)
    {
        if (link.TexturesRev == textureRev) return;
        textureRev = link.TexturesRev;
        bool sameDir = link.TexturesDir == dir;
        bool grows = sameDir && link.TextureNames.Count >= names.Count
            && names.Select((n, i) => n == link.TextureNames[i]).All(b => b);
        if (!grows)
        {
            // A new game, another computer's folder: start again. (Old atlas pages stay; they
            // are a few megabytes and a fresh game rarely comes.)
            pics = Array.Empty<Pic?>();
            state = Array.Empty<byte>();
            queue.Clear();
        }
        dir = link.TexturesDir;
        names = link.TextureNames;
        Array.Resize(ref pics, names.Count);
        Array.Resize(ref state, names.Count);
    }

    public string Name(int index) => index >= 0 && index < names.Count ? names[index] : "";

    /// <summary>Picture <paramref name="index"/>, or null while it loads (it is queued).</summary>
    public Pic? Get(int index)
    {
        if (index < 0 || index >= names.Count) return null;
        switch (state[index])
        {
            case 2: return pics[index];
            case 0:
                state[index] = 1;
                queue.Enqueue(index);
                return null;
            default: return null;
        }
    }

    /// <summary>Picture <paramref name="index"/>, loading it now if need be.</summary>
    public Pic? Now(int index)
    {
        if (index < 0 || index >= names.Count) return null;
        if (state[index] is 0 or 1) Load(index);
        return pics[index];
    }

    /// <summary>Picture <paramref name="index"/>'s pixels again, cut out, at most
    /// <paramref name="most"/> pixels on its long side, cropped to its solid part: for
    /// making a held weapon's 3D body from it. Null when it can not be read.</summary>
    public (Color[] pixels, int w, int h)? Pixels(int index, int most)
    {
        if (index < 0 || index >= names.Count) return null;
        try
        {
            var (pixels, w, h) = Img.Load(Path.Combine(dir, names[index] + ".png"));
            int factor = Math.Max(1, Math.Max(w, h) / Math.Max(4, most));
            if (factor > 1) (pixels, w, h) = Img.Shrink(pixels, w, h, factor);
            Img.HardEdges(pixels);
            Img.Bleed(pixels, w, h);
            float[]? cut = Img.Bounds(pixels, w, h);
            if (cut == null) return null;
            int x0 = (int)MathF.Round(cut[0] * w), y0 = (int)MathF.Round(cut[1] * h);
            int cw = Math.Max(1, (int)MathF.Round(cut[2] * w) - x0), ch = Math.Max(1, (int)MathF.Round(cut[3] * h) - y0);
            var crop = new Color[cw * ch];
            for (int y = 0; y < ch; y++)
                Array.Copy(pixels, (y0 + y) * w + x0, crop, y * cw, cw);
            return (crop, cw, ch);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Loads queued pictures for up to <paramref name="budgetMs"/> milliseconds.</summary>
    public void Pump(double budgetMs)
    {
        var clock = Stopwatch.StartNew();
        while (queue.Count > 0 && clock.Elapsed.TotalMilliseconds < budgetMs)
        {
            int index = queue.Dequeue();
            if (state[index] == 1) Load(index);
        }
    }

    private void Load(int index)
    {
        string name = names[index];
        try
        {
            pics[index] = Make(name, dir);
            state[index] = 2;
            Revision++;
        }
        catch (Exception e)
        {
            state[index] = 3;
            if (Revision < 100000) Console.WriteLine($"[fps] could not load picture {name}: {e.Message}");
        }
    }

    private Pic Make(string name, string folder)
    {
        var (pixels, w, h) = Img.Load(Path.Combine(folder, name + ".png"));
        bool floor = name.EndsWith("_floor", StringComparison.Ordinal);
        bool cutout = name.EndsWith("_cut", StringComparison.Ordinal);
        Pic? face = null;
        if (name.EndsWith("_l15", StringComparison.Ordinal))
        {
            string side = Path.Combine(folder, name[..^4] + "_l11.png");
            if (File.Exists(side))
            {
                var (sidePixels, sw, sh) = Img.Load(side);
                Color[]? band = sw == w && sh == h ? Img.Face(pixels, sidePixels, w, h) : null;
                if (band != null) face = Put(band, w, h, settings.PictureTexels, false);
            }
        }
        int texels = floor ? settings.FloorTexels * 16 : settings.PictureTexels;
        Pic pic = Put(pixels, w, h, texels, cutout);
        pic.Face = face;
        if (floor) pic.Span = 16;
        return pic;
    }

    /// <summary>A picture into the atlas, shrunk to <paramref name="texels"/> pixels on its
    /// long side; cut out, or made opaque so a box's top shows the thing's own colours past
    /// its outline.</summary>
    public Pic Put(Color[] pixels, int w, int h, int texels, bool cutout)
    {
        int factor = Math.Max(1, Math.Max(w, h) / Math.Max(4, texels));
        if (factor > 1) (pixels, w, h) = Img.Shrink(pixels, w, h, factor);
        var pic = new Pic { Cutout = cutout, Average = Img.Average(pixels) };
        if (cutout)
        {
            Img.HardEdges(pixels);
            pic.Cut = Img.Bounds(pixels, w, h) ?? new float[] { 0, 0, 1, 1 };
            // See-through pixels take their solid neighbours' colours, so smooth filtering
            // and mipmaps never darken the outline.
            Img.Bleed(pixels, w, h);
        }
        else
        {
            Img.Opaque(pixels, w, h);
        }
        pic.R = atlas.Add(pixels, w, h);
        return pic;
    }

    /// <summary>A picture made here, not by RimWorld (rock, water, fire, explosions...).</summary>
    public Pic Builtin(Color[] pixels, int w, int h, bool cutout)
    {
        var pic = new Pic { Cutout = cutout, Average = Img.Average(pixels) };
        if (cutout) pic.Cut = Img.Bounds(pixels, w, h) ?? new float[] { 0, 0, 1, 1 };
        pic.R = atlas.Add(pixels, w, h);
        return pic;
    }
}

/// <summary>Plain pixel work on RGBA pictures, top row first.</summary>
public static class Img
{
    public static unsafe (Color[] pixels, int w, int h) Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("no such picture", path);
        Image image = Raylib.LoadImage(path);
        if (image.Data == null || image.Width <= 0) throw new InvalidDataException("not a picture");
        try
        {
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            int w = image.Width, h = image.Height;
            var pixels = new Color[w * h];
            Color* data = (Color*)image.Data;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = data[i];
            return (pixels, w, h);
        }
        finally
        {
            Raylib.UnloadImage(image);
        }
    }

    /// <summary>Each <paramref name="f"/> by f block becomes one pixel: the average colour of
    /// its solid pixels, solid when most of it is.</summary>
    public static (Color[], int, int) Shrink(Color[] src, int w, int h, int f)
    {
        int nw = Math.Max(1, w / f), nh = Math.Max(1, h / f);
        var dst = new Color[nw * nh];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int r = 0, g = 0, b = 0, a = 0, solid = 0, n = 0;
                for (int dy = 0; dy < f; dy++)
                    for (int dx = 0; dx < f; dx++)
                    {
                        int sx = x * f + dx, sy = y * f + dy;
                        if (sx >= w || sy >= h) continue;
                        Color c = src[sy * w + sx];
                        n++;
                        a += c.A;
                        if (c.A < 40) continue;
                        r += c.R * c.A;
                        g += c.G * c.A;
                        b += c.B * c.A;
                        solid += c.A;
                    }
                dst[y * nw + x] = solid == 0
                    ? new Color(0, 0, 0, 0)
                    : new Color(r / solid, g / solid, b / solid, a / Math.Max(1, n));
            }
        return (dst, nw, nh);
    }

    public static void HardEdges(Color[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            pixels[i] = c.A >= 110 ? new Color(c.R, c.G, c.B, (byte)255) : new Color(c.R, c.G, c.B, (byte)0);
        }
    }

    public static float[]? Bounds(Color[] pixels, int w, int h)
    {
        int x0 = w, y0 = h, x1 = 0, y1 = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[y * w + x].A >= 128)
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x + 1);
                    y1 = Math.Max(y1, y + 1);
                }
        return x1 > x0 && y1 > y0 ? new[] { x0 / (float)w, y0 / (float)h, x1 / (float)w, y1 / (float)h } : null;
    }

    public static Color Average(Color[] pixels)
    {
        long r = 0, g = 0, b = 0, n = 0;
        foreach (Color c in pixels)
        {
            if (c.A < 128) continue;
            r += c.R;
            g += c.G;
            b += c.B;
            n++;
        }
        return n == 0 ? new Color(128, 128, 128, 255) : new Color((int)(r / n), (int)(g / n), (int)(b / n), 255);
    }

    /// <summary>See-through pixels take the colour of the solid ones nearby, staying
    /// see-through.</summary>
    public static void Bleed(Color[] pixels, int w, int h)
    {
        var alpha = pixels.Select(p => p.A).ToArray();
        Opaque(pixels, w, h);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(pixels[i].R, pixels[i].G, pixels[i].B, alpha[i]);
    }

    /// <summary>See-through pixels take the colour of the solid ones next to them, a few
    /// pixels out, then the solid ones' mean colour; all become opaque.</summary>
    public static void Opaque(Color[] pixels, int w, int h)
    {
        var solid = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) solid[i] = pixels[i].A >= 128;
        Color mean = Average(pixels);
        for (int pass = 0; pass < 8; pass++)
        {
            var before = (bool[])solid.Clone();
            bool grew = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (before[i]) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    void Take(int nx, int ny)
                    {
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                        int j = ny * w + nx;
                        if (!before[j]) return;
                        r += pixels[j].R;
                        g += pixels[j].G;
                        b += pixels[j].B;
                        n++;
                    }
                    Take(x - 1, y);
                    Take(x + 1, y);
                    Take(x, y - 1);
                    Take(x, y + 1);
                    if (n == 0) continue;
                    pixels[i] = new Color(r / n, g / n, b / n, 255);
                    solid[i] = true;
                    grew = true;
                }
            if (!grew) break;
        }
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = solid[i] ? new Color(pixels[i].R, pixels[i].G, pixels[i].B, (byte)255) : mean;
    }

    /// <summary>
    /// A linked wall's face, as RimWorld draws it from above: the band along the bottom of its
    /// piece open to the south (<paramref name="side"/>) that its all-linked piece
    /// (<paramref name="top"/>) lacks, stretched to fill the picture, so it covers the wall's
    /// side from floor to top. Null when the two do not differ along the bottom.
    /// </summary>
    public static Color[]? Face(Color[] top, Color[] side, int w, int h)
    {
        float[] Pixel(Color c)
        {
            float a = c.A / 255f;
            return new[] { c.R * a, c.G * a, c.B * a };
        }
        float Difference(int y)
        {
            float total = 0;
            for (int x = 0; x < w; x++)
            {
                float[] a = Pixel(top[y * w + x]), b = Pixel(side[y * w + x]);
                total += (Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) + Math.Abs(a[2] - b[2])) / 3f;
            }
            return total / w;
        }
        bool Solid(int y)
        {
            int n = 0;
            for (int x = 0; x < w; x++) if (side[y * w + x].A >= 128) n++;
            return n * 2 > w;
        }
        float Light(int y)
        {
            float total = 0;
            for (int x = 0; x < w; x++)
            {
                float[] p = Pixel(side[y * w + x]);
                total += p[0] + p[1] + p[2];
            }
            return total / w;
        }
        int bottom = h;
        while (bottom > 0 && !Solid(bottom - 1)) bottom--;
        int first = bottom, gap = 0;
        for (int y = bottom - 1; y >= 0; y--)
        {
            if (Difference(y) >= 10f)
            {
                first = y;
                gap = 0;
            }
            else if (++gap > 3) break;
        }
        int rows = h / 21; // six rows of a 128 pixel piece
        if (bottom - first < Math.Max(3, rows)) return null;
        var light = Enumerable.Range(first, bottom - first).Select(Light).OrderBy(v => v).ToList();
        float usual = light[light.Count / 2];
        while (bottom - first > Math.Max(3, rows) && Light(bottom - 1) < usual * 0.7f) bottom--;
        // The band, opaque, stretched to the whole picture (nearest, to keep it crisp).
        var band = new Color[w * (bottom - first)];
        Array.Copy(side, first * w, band, 0, band.Length);
        Opaque(band, w, bottom - first);
        var face = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            int from = first + (int)((y + 0.5f) * (bottom - first) / h) - first;
            Array.Copy(band, Math.Clamp(from, 0, bottom - first - 1) * w, face, y * w, w);
        }
        return face;
    }
}
