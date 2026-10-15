using System.Numerics;
using RimFps.World;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// Drawing helpers for the HUD and menus, in RimWorld's manner: dark see-through panels with a
/// thin light edge, plain white text, green-yellow-red bars. Sizes are in pixels; <see cref="S"/>
/// scales a size meant for a 1080 line screen to the window. The font is the system's own clean sans (Segoe UI, Arial or
/// DejaVu Sans), else raylib's built-in one.
/// </summary>
public static class Ui
{
    public static readonly Color White = new(236, 236, 236, 255);
    public static readonly Color Grey = new(168, 170, 172, 255);
    public static readonly Color Dim = new(120, 122, 126, 255);
    public static readonly Color Gold = new(232, 196, 86, 255);
    public static readonly Color Red = new(214, 52, 40, 255);
    public static readonly Color Green = new(112, 196, 92, 255);
    public static readonly Color Orange = new(230, 140, 50, 255);
    public static readonly Color Blue = new(110, 160, 230, 255);
    public static readonly Color PanelFill = new(22, 23, 25, 200);
    public static readonly Color PanelEdge = new(96, 99, 103, 230);
    public static readonly Color Shadow = new(0, 0, 0, 170);

    private static Font font;
    private static bool loaded;

    /// <summary>Screen pixels per 1080p pixel.</summary>
    public static float Scale { get; private set; } = 1f;

    public static float S(float v) => v * Scale;

    public static void Begin(int screenH) => Scale = Math.Max(0.5f, screenH / 1080f);

    public static void Load()
    {
        string[] candidates =
        {
            @"C:\Windows\Fonts\segoeuib.ttf", @"C:\Windows\Fonts\segoeui.ttf", @"C:\Windows\Fonts\arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf", "/Library/Fonts/Arial.ttf",
            "/system/fonts/Roboto-Medium.ttf", "/system/fonts/Roboto-Regular.ttf", "/system/fonts/NotoSans-Regular.ttf", "/system/fonts/DroidSans.ttf",
        };
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            font = Raylib.LoadFontEx(path, 48, null, 0);
            if (font.Texture.Id == 0) continue;
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
            loaded = true;
            return;
        }
        font = Raylib.GetFontDefault();
        loaded = true;
    }

    public static void Unload()
    {
        if (loaded && font.Texture.Id != Raylib.GetFontDefault().Texture.Id) Raylib.UnloadFont(font);
    }

    /// <summary>Text <paramref name="size"/> pixels tall (scale 1080p sizes with <see cref="S"/>),
    /// with a soft drop shadow.</summary>
    public static void Text(string text, float x, float y, float size, Color color, bool shadow = true)
    {
        float px = MathF.Max(8f, MathF.Round(size));
        if (shadow) Raylib.DrawTextEx(font, text, new Vector2(MathF.Round(x) + 1, MathF.Round(y) + 1), px, 0, new Color((byte)0, (byte)0, (byte)0, (byte)(color.A * 0.7f)));
        Raylib.DrawTextEx(font, text, new Vector2(MathF.Round(x), MathF.Round(y)), px, 0, color);
    }

    public static float Width(string text, float size) => Raylib.MeasureTextEx(font, text, MathF.Max(8f, MathF.Round(size)), 0).X;

    public static void Centred(string text, float cx, float y, float size, Color color) => Text(text, cx - Width(text, size) / 2, y, size, color);

    public static void Right(string text, float rx, float y, float size, Color color) => Text(text, rx - Width(text, size), y, size, color);

    /// <summary>A RimWorld window: dark, see-through, a thin light edge.</summary>
    public static void Panel(Rectangle r, Color? fill = null)
    {
        Raylib.DrawRectangleRec(r, fill ?? PanelFill);
        Raylib.DrawRectangleLinesEx(r, Math.Max(1f, MathF.Round(Scale)), PanelEdge);
    }

    /// <summary>A bar filled to <paramref name="share"/>, as RimWorld's need and health bars.</summary>
    public static void Bar(float x, float y, float w, float h, float share, Color color)
    {
        share = Math.Clamp(share, 0f, 1f);
        Raylib.DrawRectangleRec(new Rectangle(x, y, w, h), new Color(10, 10, 12, 180));
        Raylib.DrawRectangleRec(new Rectangle(x + 1, y + 1, (w - 2) * share, h - 2), color);
    }

    /// <summary>RimWorld's health colours: green fine, yellow hurt, orange bad, red near gone.</summary>
    public static Color Health(float share) => share switch
    {
        >= 0.85f => Green,
        >= 0.6f => new Color(196, 206, 80, 255),
        >= 0.35f => Orange,
        > 0f => Red,
        _ => new Color(90, 90, 92, 255),
    };

    public static Color Fade(Color c, float a) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * a, 0, 255));

    public static Color Shade(Color c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255), c.A);

    /// <summary>The source rectangle of a picture in its atlas page, in pixels: its solid part,
    /// or all of it.</summary>
    public static Rectangle Source(Pic pic, bool cut = true)
    {
        float s = Atlas.PageSize;
        float u0 = pic.R.U0 * s, v0 = pic.R.V0 * s, w = (pic.R.U1 - pic.R.U0) * s, h = (pic.R.V1 - pic.R.V0) * s;
        if (!cut) return new Rectangle(u0, v0, w, h);
        return new Rectangle(u0 + pic.Cut[0] * w, v0 + pic.Cut[1] * h, pic.CutW * w, pic.CutH * h);
    }

    /// <summary>Draws a picture into <paramref name="dest"/>, turned about <paramref name="origin"/>
    /// (in dest's pixels), mirrored with <paramref name="flip"/>.</summary>
    public static void Picture(Atlas atlas, Pic pic, Rectangle dest, Color tint, bool flip = false, float rotation = 0, Vector2 origin = default, bool cut = true)
    {
        Rectangle src = Source(pic, cut);
        if (flip) src.Width = -src.Width;
        Raylib.DrawTexturePro(atlas.Texture(pic.R.Page), src, dest, origin, rotation, tint);
    }

    /// <summary>A picture fitted inside <paramref name="box"/>, its proportions kept, centred.</summary>
    public static Rectangle Fit(Atlas atlas, Pic pic, Rectangle box, Color tint, bool flip = false)
    {
        Rectangle src = Source(pic);
        float k = Math.Min(box.Width / Math.Max(1f, src.Width), box.Height / Math.Max(1f, src.Height));
        var dest = new Rectangle(box.X + (box.Width - src.Width * k) / 2, box.Y + (box.Height - src.Height * k) / 2, src.Width * k, src.Height * k);
        Picture(atlas, pic, dest, tint, flip);
        return dest;
    }
}
