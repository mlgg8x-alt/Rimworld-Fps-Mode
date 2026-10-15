using Raylib_cs;

namespace RimFps.Render;

/// <summary>
/// The colours the screen may show. Like a 90s shooter's palette it is made of ramps: a run
/// of shades, dark to light, for each of sixteen hues picked for RimWorld's earthy art
/// (stone, soil, wood, leaf, skin, blood, fire, sky...). A lookup table maps every colour
/// (32 levels a channel) to its nearest palette colour, for the post-processing shader.
/// </summary>
public static class Palette
{
    private static readonly (int r, int g, int b)[] Hues =
    {
        (200, 200, 200), // grey
        (184, 172, 150), // stone
        (156, 106, 64),  // brown
        (226, 186, 146), // skin, sand
        (110, 74, 44),   // dark wood
        (146, 146, 72),  // olive
        (92, 158, 64),   // leaf
        (48, 104, 56),   // pine
        (64, 146, 146),  // teal
        (120, 168, 226), // sky
        (56, 84, 186),   // blue
        (128, 86, 166),  // purple
        (196, 44, 34),   // blood
        (236, 134, 42),  // fire
        (244, 224, 96),  // yellow
        (222, 128, 126), // pink
    };

    /// <summary>The palette's colours: <paramref name="shades"/> per hue.</summary>
    public static List<Color> Make(int shades)
    {
        var colors = new List<Color>();
        foreach (var (r, g, b) in Hues)
            for (int i = 0; i < shades; i++)
            {
                float k = shades == 1 ? 1f : i / (float)(shades - 1);
                float bright = 0.05f + k * 1.12f;
                float cr, cg, cb;
                if (bright <= 1f)
                {
                    // Darker shades drift a touch cooler, as hand-made ramps do.
                    cr = r * bright * (1f - (1f - bright) * 0.12f);
                    cg = g * bright;
                    cb = b * bright * (1f + (1f - bright) * 0.1f);
                }
                else
                {
                    float t = (bright - 1f) * 3.5f;
                    cr = r + (255 - r) * t;
                    cg = g + (255 - g) * t;
                    cb = b + (255 - b) * t;
                }
                colors.Add(new Color((byte)Math.Clamp(cr, 0, 255), (byte)Math.Clamp(cg, 0, 255), (byte)Math.Clamp(cb, 0, 255), (byte)255));
            }
        colors.Add(new Color(0, 0, 0, 255));
        colors.Add(new Color(255, 255, 255, 255));
        return colors;
    }

    /// <summary>The lookup table as a 1024 by 32 picture: x is blue * 32 + red, y is green,
    /// each in 32 levels; each pixel the nearest palette colour.</summary>
    public static Color[] Lookup(List<Color> palette)
    {
        var lut = new Color[1024 * 32];
        Parallel.For(0, 32, g =>
        {
            for (int b = 0; b < 32; b++)
                for (int r = 0; r < 32; r++)
                {
                    float cr = r * 255f / 31f, cg = g * 255f / 31f, cb = b * 255f / 31f;
                    Color best = palette[0];
                    float bestDistance = float.MaxValue;
                    foreach (Color p in palette)
                    {
                        // Weighted towards green, which the eye sees most.
                        float dr = p.R - cr, dg = p.G - cg, db = p.B - cb;
                        float mean = (p.R + cr) * 0.5f;
                        float d = (2f + mean / 256f) * dr * dr + 4f * dg * dg + (2f + (255f - mean) / 256f) * db * db;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = p;
                        }
                    }
                    lut[g * 1024 + b * 32 + r] = best;
                }
        });
        return lut;
    }
}
