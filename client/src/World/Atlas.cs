using System.Numerics;
using Raylib_cs;

namespace RimFps.World;

/// <summary>Where a picture sits in the atlas: its page and its corners' texture
/// coordinates.</summary>
public readonly record struct Region(int Page, float U0, float V0, float U1, float V1, int W, int H)
{
    /// <summary>The texture coordinates of point (u, v) of the picture, each 0 to 1 across
    /// it, top left first.</summary>
    public Vector2 At(float u, float v) => new(U0 + (U1 - U0) * u, V0 + (V1 - V0) * v);
}

/// <summary>
/// Every picture the client draws, packed into a few big textures (pages), so the world and
/// its sprites draw in a handful of batches. In RimWorld style the pages are smooth: each
/// picture has a border of its own edge pixels around it, so mipmaps and linear filtering do
/// not bleed one picture into the next. In retro style they are point-sampled, one texel one
/// chunky block of colour.
/// </summary>
public sealed class Atlas : IDisposable
{
    public const int PageSize = 2048;

    private sealed class Page
    {
        public Texture2D Texture;
        /// <summary>Rows by height, each with how far along it is filled.</summary>
        public readonly List<(int y, int h, int x)> Rows = new();
        public int Top;
        public bool Dirty;
    }

    private readonly List<Page> pages = new();
    private readonly bool smooth;
    private readonly int gutter;
    private float sinceMipmaps;

    public Atlas(bool smooth)
    {
        this.smooth = smooth;
        gutter = smooth ? 4 : 0;
    }

    public int Pages => pages.Count;

    public Texture2D Texture(int page) => pages[page].Texture;

    /// <summary>Puts a picture of w by h pixels (RGBA, top row first) in the atlas.</summary>
    public Region Add(Color[] pixels, int w, int h)
    {
        w = Math.Clamp(w, 1, PageSize - 2 * gutter);
        h = Math.Clamp(h, 1, PageSize - 2 * gutter);
        int bw = w + 2 * gutter, bh = h + 2 * gutter;
        for (int p = 0; p < pages.Count; p++)
            if (TryPlace(pages[p], bw, bh, out int x, out int y))
                return Upload(p, x, y, pixels, w, h);
        pages.Add(NewPage());
        int last = pages.Count - 1;
        if (!TryPlace(pages[last], bw, bh, out int nx, out int ny))
            throw new InvalidOperationException($"a {w}x{h} picture does not fit an atlas page");
        return Upload(last, nx, ny, pixels, w, h);
    }

    private static bool TryPlace(Page page, int w, int h, out int x, out int y)
    {
        for (int r = 0; r < page.Rows.Count; r++)
        {
            var (ry, rh, rx) = page.Rows[r];
            if (rh == h && rx + w <= PageSize)
            {
                page.Rows[r] = (ry, rh, rx + w);
                x = rx;
                y = ry;
                return true;
            }
        }
        if (page.Top + h <= PageSize)
        {
            page.Rows.Add((page.Top, h, w));
            x = 0;
            y = page.Top;
            page.Top += h;
            return true;
        }
        x = y = 0;
        return false;
    }

    private Page NewPage()
    {
        Image blank = Raylib.GenImageColor(PageSize, PageSize, new Color(128, 128, 128, 0));
        Texture2D texture = Raylib.LoadTextureFromImage(blank);
        Raylib.UnloadImage(blank);
        Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
        Raylib.SetTextureFilter(texture, smooth ? TextureFilter.Bilinear : TextureFilter.Point);
        return new Page { Texture = texture };
    }

    private unsafe Region Upload(int page, int x, int y, Color[] pixels, int w, int h)
    {
        int g = gutter, bw = w + 2 * g, bh = h + 2 * g;
        Color[] padded = pixels;
        if (g > 0)
        {
            // The picture's edge pixels carried out into its border.
            padded = new Color[bw * bh];
            for (int py = 0; py < bh; py++)
            {
                int sy = Math.Clamp(py - g, 0, h - 1);
                for (int px = 0; px < bw; px++)
                    padded[py * bw + px] = pixels[sy * w + Math.Clamp(px - g, 0, w - 1)];
            }
        }
        fixed (Color* data = padded)
            Raylib.UpdateTextureRec(pages[page].Texture, new Rectangle(x, y, bw, bh), data);
        pages[page].Dirty = true;
        // Retro: a hair inside the edge, so point sampling never reaches the neighbour.
        float inset = smooth ? 0f : 0.02f;
        float s = PageSize;
        return new Region(page, (x + g + inset) / s, (y + g + inset) / s, (x + g + w - inset) / s, (y + g + h - inset) / s, w, h);
    }

    /// <summary>Makes the mipmaps again for pages that changed, at most four times a second.</summary>
    public void Refresh(float dt)
    {
        if (!smooth) return;
        sinceMipmaps += dt;
        if (sinceMipmaps < 0.25f) return;
        foreach (Page page in pages)
        {
            if (!page.Dirty) continue;
            page.Dirty = false;
            Raylib.GenTextureMipmaps(ref page.Texture);
            Raylib.SetTextureFilter(page.Texture, TextureFilter.Trilinear);
            Raylib.SetTextureFilter(page.Texture, TextureFilter.Anisotropic8X);
            sinceMipmaps = 0;
        }
    }

    public void Dispose()
    {
        foreach (Page page in pages) Raylib.UnloadTexture(page.Texture);
        pages.Clear();
    }
}
