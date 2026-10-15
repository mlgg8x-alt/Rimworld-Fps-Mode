using Raylib_cs;

namespace RimFps.Render;

/// <summary>
/// Pictures the client paints itself, in the spirit of a 90s software renderer's art: the
/// ones RimWorld has no art for (natural rock, water, ceilings, door slabs) and every effect
/// (fire, explosions, smoke, muzzle flashes, sparks, rain). Each is a small RGBA picture,
/// top row first, deterministic, so the world looks the same every time.
/// </summary>
public static class PixelArt
{
    private static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    /// <summary>White noise, 0 to 1.</summary>
    public static float Rand(int x, int y, int seed) => (Hash(x, y, seed) & 0xffff) / 65535f;

    /// <summary>Smooth value noise that wraps every <paramref name="period"/> pixels.</summary>
    public static float Noise(float x, float y, int period, int seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        float V(int ix, int iy) => Rand(((ix % period) + period) % period, ((iy % period) + period) % period, seed);
        float a = V(x0, y0), b = V(x0 + 1, y0), c = V(x0, y0 + 1), d = V(x0 + 1, y0 + 1);
        return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
    }

    /// <summary>Layered noise that tiles a <paramref name="size"/> picture, 0 to 1.</summary>
    public static float Fbm(int x, int y, int size, int cells, int seed)
    {
        float total = 0, amp = 0.5f, sum = 0;
        int period = cells;
        for (int o = 0; o < 4 && period <= size; o++)
        {
            float f = period / (float)size;
            total += Noise(x * f, y * f, period, seed + o * 31) * amp;
            sum += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return total / sum;
    }

    public static Color Shade(Color c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255), c.A);

    public static Color Mix(Color a, Color b, float t) =>
        new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));

    /// <summary>Natural rock: blocky stone with cracks and chipped highlights, like a cave
    /// wall in an old shooter.</summary>
    public static Color[] Rock(int size, Color baseColor, int seed)
    {
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Fbm(x, y, size, 4, seed);
                float grain = Rand(x, y, seed + 7);
                // Cracks: thin dark seams along the noise's valleys.
                float seam = MathF.Abs(Fbm(x, y, size, 8, seed + 99) - 0.5f);
                float k = 0.72f + n * 0.5f + (grain - 0.5f) * 0.12f;
                if (seam < 0.025f) k *= 0.55f;
                else if (seam < 0.04f) k *= 1.12f;
                px[y * size + x] = Shade(baseColor, k);
            }
        return px;
    }

    /// <summary>Water, its ripples quantized into bands; deep water darker and calmer.</summary>
    public static Color[] Water(int size, bool deep, int frame)
    {
        var px = new Color[size * size];
        Color basis = deep ? new Color(28, 52, 92, 255) : new Color(54, 96, 130, 255);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Fbm(x + frame * 3, y + frame, size, 4, 41);
                float wave = MathF.Sin((x + y * 0.5f + frame * 4) * 0.39f) * 0.5f + 0.5f;
                float k = 0.8f + MathF.Floor((n * 0.6f + wave * 0.4f) * 4f) / 4f * 0.45f;
                px[y * size + x] = Shade(basis, k);
            }
        return px;
    }

    /// <summary>Plain ground for a cell RimWorld sent no art for.</summary>
    public static Color[] Ground(int size, Color basis, int seed)
    {
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Fbm(x, y, size, 4, seed);
                float grain = Rand(x, y, seed);
                px[y * size + x] = Shade(basis, 0.78f + n * 0.4f + (grain > 0.9f ? 0.12f : grain < 0.08f ? -0.12f : 0f));
            }
        return px;
    }

    /// <summary>Boards: a built roof's underside, or a fallback floor.</summary>
    public static Color[] Planks(int size, Color basis, int seed)
    {
        var px = new Color[size * size];
        int board = size / 4;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int row = y / board;
                int offset = (int)(Rand(row, 0, seed) * size);
                bool seam = y % board == 0 || (x + offset) % size == 0;
                float grain = Noise(x * 0.5f, y * 4f, size, seed + row) * 0.25f;
                float k = seam ? 0.5f : 0.82f + grain + (Rand(row, 1, seed) - 0.5f) * 0.15f;
                px[y * size + x] = Shade(basis, k);
            }
        return px;
    }

    /// <summary>A grey door slab with a frame, panels and rivets, tinted by the door's own
    /// colour when drawn: a classic sliding door.</summary>
    public static Color[] Door(int w, int h)
    {
        var px = new Color[w * h];
        Color basis = new(190, 190, 190, 255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = 0.85f + (Rand(x, y, 5) - 0.5f) * 0.08f;
                bool frame = x < 3 || y < 3 || x >= w - 3 || y >= h - 3;
                bool panel = (x > 6 && x < w / 2 - 3 || x > w / 2 + 3 && x < w - 7) && y > 8 && y < h - 8 && (y < h / 2 - 3 || y > h / 2 + 3);
                bool bevel = panel && (x == 7 || x == w / 2 + 4 || y == 9 || y == h / 2 + 4);
                bool rivet = (x == 4 || x == w - 5) && y % 8 == 4;
                if (frame) k = 0.6f;
                else if (bevel) k = 1.15f;
                else if (panel) k = 0.72f;
                if (rivet) k = 1.3f;
                if (x == w / 2 || x == w / 2 - 1) k = 0.4f;
                px[y * w + x] = Shade(basis, k);
            }
        return px;
    }

    /// <summary>A machine with no picture of its own: riveted grey metal.</summary>
    public static Color[] Metal(int size)
    {
        var px = new Color[size * size];
        Color basis = new(130, 134, 140, 255);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float k = 0.8f + Fbm(x, y, size, 8, 3) * 0.3f;
                if (x % (size / 2) == 0 || y % (size / 2) == 0) k = 0.55f;
                if ((x % (size / 2) == 3 || x % (size / 2) == size / 2 - 3) && (y % (size / 2) == 3 || y % (size / 2) == size / 2 - 3)) k = 1.25f;
                px[y * size + x] = Shade(basis, k);
            }
        return px;
    }

    /// <summary>A round, soft-edged puff in quantized rings: smoke, dust, gas.</summary>
    public static Color[] Puff(int size, Color basis, int seed)
    {
        var px = new Color[size * size];
        float c = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float wobble = Fbm(x, y, size, 4, seed) * 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy) + wobble * 0.5f - 0.15f;
                if (d > 0.95f) continue;
                float k = 1.05f - MathF.Floor(d * 4f) / 4f * 0.35f;
                px[y * size + x] = Shade(basis, k);
            }
        return px;
    }

    /// <summary>One frame of a fireball, from a white-hot flash (frame 0) to a dark,
    /// smoky ball breaking up (the last). Quantized to a few fire colours.</summary>
    public static Color[] Fireball(int size, int frame, int frames, int seed)
    {
        Color[] ramp =
        {
            new(255, 255, 220, 255), new(255, 236, 120, 255), new(255, 186, 52, 255),
            new(232, 112, 24, 255), new(176, 52, 16, 255), new(96, 34, 22, 255), new(54, 46, 44, 255),
        };
        var px = new Color[size * size];
        float t = frame / (float)Math.Max(1, frames - 1);
        float c = size / 2f;
        float radius = 0.45f + 0.5f * MathF.Sqrt(t);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float n = Fbm(x + frame * 5, y - frame * 7, size, 4, seed);
                float d = MathF.Sqrt(dx * dx + dy * dy) / radius + (n - 0.5f) * 0.8f;
                if (d > 1f) continue;
                // Hot in the middle early on, cooling outward and over time.
                float heat = (1f - d) * (1.3f - t * 1.1f) + n * 0.25f;
                int i = Math.Clamp((int)((1f - heat) * ramp.Length), 0, ramp.Length - 1);
                if (t > 0.6f && Rand(x, y, seed + frame) < (t - 0.6f) * 1.5f) continue; // breaking up
                px[y * size + x] = ramp[i];
            }
        return px;
    }

    /// <summary>One frame of a flickering flame: a teardrop of fire with tongues licking up,
    /// white-yellow at its heart, red at its edges.</summary>
    public static Color[] Flame(int w, int h, int frame, int seed)
    {
        Color[] ramp = { new(255, 250, 210, 255), new(255, 222, 96, 255), new(252, 160, 40, 255), new(220, 84, 24, 255), new(150, 40, 20, 255) };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float up = 1f - (y + 0.5f) / h;               // 0 at the bottom, 1 at the top
                float across = ((x + 0.5f) / w - 0.5f) * 2f;   // -1 to 1
                // Tongues: the flame sways and splits as it rises.
                float sway = (Noise(up * 3f + frame * 0.9f, seed * 0.1f, 64, seed) - 0.5f) * 0.8f * up;
                float a = across - sway;
                // A teardrop: a rounded base, widest two fifths of the way up, a point at the top.
                float profile = MathF.Pow(MathF.Max(0f, MathF.Sin(MathF.PI * Math.Clamp(0.18f + up * 0.82f, 0f, 1f))), 0.75f) * 0.95f;
                float ragged = (Noise(x * 0.6f, y * 0.35f - frame * 2.3f, 64, seed + 5) - 0.5f) * 0.55f;
                float edge = MathF.Abs(a) / MathF.Max(profile, 0.01f) + ragged;
                if (edge > 1f) continue;
                float heat = (1f - edge) * (1.15f - up * 0.9f);
                int i = Math.Clamp((int)((1f - heat) * ramp.Length), 0, ramp.Length - 1);
                px[y * w + x] = ramp[i];
            }
        return px;
    }

    /// <summary>A hand as RimWorld draws one: a round blob with a dark outline, white so it can
    /// be tinted to any skin.</summary>
    public static Color[] Hand(int size)
    {
        var px = new Color[size * size];
        float c = size / 2f, r = size / 2f - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                float shade = 1f - Math.Clamp((dx + dy) / (r * 2.4f), -0.12f, 0.2f);
                byte v = (byte)Math.Clamp(242 * shade, 0, 255);
                px[y * size + x] = d > r - size * 0.09f ? new Color(34, 28, 24, 255) : new Color(v, v, v, (byte)255);
            }
        return px;
    }

    /// <summary>A muzzle flash: a white core and a star of yellow spikes.</summary>
    public static Color[] MuzzleFlash(int size, int seed)
    {
        var px = new Color[size * size];
        float c = size / 2f;
        int spikes = 7;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = MathF.Sqrt(dx * dx + dy * dy) / c;
                float a = MathF.Atan2(dy, dx);
                float spike = MathF.Pow(MathF.Abs(MathF.Cos(a * spikes / 2f + Rand(0, 0, seed) * 6f)), 6f);
                float reach = 0.35f + spike * 0.65f * (0.7f + Rand((int)(a * 10), 0, seed) * 0.3f);
                if (d > reach) continue;
                px[y * size + x] = d < 0.25f ? new Color(255, 255, 235, 255) : d < reach * 0.7f ? new Color(255, 226, 90, 255) : new Color(255, 150, 40, 255);
            }
        return px;
    }

    /// <summary>A small hot glow: a bullet in flight, a spark.</summary>
    public static Color[] Glow(int size, Color hot, Color rim)
    {
        var px = new Color[size * size];
        float c = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 1f) continue;
                px[y * size + x] = d < 0.5f ? hot : rim;
            }
        return px;
    }

    /// <summary>A splash of blood or grit: a blob with drops thrown out.</summary>
    public static Color[] Splat(int size, Color basis, int seed)
    {
        var px = new Color[size * size];
        float c = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float n = Fbm(x, y, size, 4, seed);
                bool blob = d < 0.35f + n * 0.4f;
                bool drop = Rand(x / 2, y / 2, seed) > 0.93f && d < 0.95f;
                if (!blob && !drop) continue;
                px[y * size + x] = Shade(basis, 0.75f + n * 0.5f);
            }
        return px;
    }

    /// <summary>An electric crackle for EMP blasts.</summary>
    public static Color[] Crackle(int size, int frame)
    {
        var px = new Color[size * size];
        var bright = new Color(200, 240, 255, 255);
        var blue = new Color(80, 150, 255, 255);
        for (int bolt = 0; bolt < 7; bolt++)
        {
            float a = bolt / 7f * MathF.Tau + frame;
            float x = size / 2f, y = size / 2f;
            for (int step = 0; step < size / 2; step++)
            {
                a += (Rand(bolt, step, frame) - 0.5f) * 1.4f;
                x += MathF.Cos(a);
                y += MathF.Sin(a);
                int ix = (int)x, iy = (int)y;
                if (ix < 0 || iy < 0 || ix >= size || iy >= size) break;
                px[iy * size + ix] = step < size / 5 ? bright : blue;
            }
        }
        return px;
    }

    public static Color[] Streak(int w, int h, Color color)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Math.Abs(x - w / 2) <= 0) px[y * w + x] = color;
        return px;
    }

    public static Color[] Solid(int w, int h, Color color)
    {
        var px = new Color[w * h];
        Array.Fill(px, color);
        return px;
    }

    /// <summary>
    /// The sky all the way round, 360 degrees across: bands of colour, pixel clouds and a ridge
    /// of far hills along the bottom, in the style of a 90s shooter's sky texture. Tinted for
    /// the time of day and the weather when drawn.
    /// </summary>
    public static Color[] Sky(int w, int h)
    {
        var px = new Color[w * h];
        Color top = new(78, 112, 168, 255), horizon = new(170, 196, 214, 255);
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            Color band = Mix(top, horizon, MathF.Floor(t * 7f) / 7f);
            for (int x = 0; x < w; x++) px[y * w + x] = band;
        }
        // Clouds: thresholded noise in two shades.
        for (int y = 0; y < h * 3 / 4; y++)
            for (int x = 0; x < w; x++)
            {
                float n = Noise(x / 24f, y / 9f, w / 24, 77) * 0.65f + Noise(x / 8f, y / 4f, w / 8, 78) * 0.35f;
                float fade = 1f - y / (h * 0.75f);
                if (n * (0.7f + fade * 0.5f) > 0.62f) px[y * w + x] = n > 0.7f ? new Color(236, 238, 240, 255) : new Color(200, 208, 218, 255);
            }
        // Far hills, then nearer ones, along the bottom.
        for (int x = 0; x < w; x++)
        {
            float far = h - 22 - Noise(x / 40f, 0, w / 40, 5) * 26f - Noise(x / 9f, 0, w / 9, 6) * 5f;
            float near = h - 10 - Noise(x / 25f, 3, w / 25, 8) * 16f;
            for (int y = 0; y < h; y++)
            {
                if (y >= near) px[y * w + x] = new Color(58, 76, 64, 255);
                else if (y >= far) px[y * w + x] = new Color(92, 110, 118, 255);
            }
        }
        return px;
    }

    /// <summary>
    /// A soft sky all the way round for RimWorld style: a smooth gradient, soft round clouds
    /// and a faint far ridge, made to be drawn with smooth filtering.
    /// </summary>
    public static Color[] SoftSky(int w, int h)
    {
        var px = new Color[w * h];
        Color top = new(92, 132, 190, 255), horizon = new(196, 214, 228, 255);
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            Color band = Mix(top, horizon, t * t);
            for (int x = 0; x < w; x++)
            {
                // Whole periods across the width, so the sky meets itself all the way round.
                float n = Noise(x / 128f, y / 40f, w / 128, 77) * 0.6f + Noise(x / 32f, y / 12f, w / 32, 78) * 0.3f + Noise(x / 16f, y / 6f, w / 16, 79) * 0.1f;
                float fade = Math.Clamp(1f - y / (h * 0.82f), 0f, 1f);
                float cloud = Math.Clamp((n - 0.5f) * 4f, 0f, 1f) * fade;
                Color c = Mix(band, new Color(246, 246, 248, 255), cloud * 0.85f);
                px[y * w + x] = c;
            }
        }
        for (int x = 0; x < w; x++)
        {
            float ridge = h - 14 - Noise(x / 128f, 0, w / 128, 5) * 22f - Noise(x / 32f, 0, w / 32, 6) * 5f;
            for (int y = (int)ridge; y < h; y++)
                px[y * w + x] = Mix(px[y * w + x], new Color(124, 142, 150, 255), Math.Clamp((y - ridge) / 3f, 0f, 1f) * 0.75f);
        }
        return px;
    }

    /// <summary>Stars for the night sky, sparse white pixels on nothing.</summary>
    public static Color[] Stars(int w, int h)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Rand(x, y, 123) > 0.996f) px[y * w + x] = Rand(x, y, 124) > 0.5f ? new Color(255, 255, 255, 255) : new Color(170, 180, 220, 255);
        return px;
    }
}
