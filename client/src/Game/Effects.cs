using System.Numerics;
using RimFps.Net;
using RimFps.Render;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// Everything short-lived drawn in the world: puffs and sparks where bullets land, muzzle
/// flashes, explosions (a sprite fireball, as in Doom, not particles), RimWorld's fires and
/// gas, rain and snow around the player, filth on the ground and RimWorld's own flecks. In
/// RimWorld coordinates (x east, z north, y up, in cells).
/// </summary>
public sealed class Effects
{
    public enum Kind { Puff, Spark, Flash, Boom, Emp, Smoke, Blood, Grit, Foam, Fleck }

    private sealed class Bit
    {
        public Kind Kind;
        public Vector3 At, Vel;
        public float Age, Life, Size, Grow, Spin, Rot;
        public bool Bright, Flat;
        public Color Color = Color.White;
        public Pic? Pic;
        public float FadeIn, Solid, FadeOut;
    }

    private readonly List<Bit> bits = new();
    private readonly Random random = new(7);
    private float time;
    /// <summary>Screen shake from nearby blasts, 0 to 1, fading.</summary>
    public float Shake;
    /// <summary>The flash of a nearby blast, 0 to 1, fading.</summary>
    public float Blast;

    public int Count => bits.Count;

    private float R(float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);

    public void Puff(Vector3 at, Kind kind, float size = 0.35f, int count = 3)
    {
        for (int i = 0; i < count && bits.Count < 1500; i++)
            bits.Add(new Bit
            {
                Kind = kind,
                At = at + new Vector3(R(-0.08f, 0.08f), R(0, 0.1f), R(-0.08f, 0.08f)),
                Vel = new Vector3(R(-0.4f, 0.4f), R(0.3f, 1.0f), R(-0.4f, 0.4f)) * (kind == Kind.Smoke ? 0.5f : 1f),
                Life = kind switch { Kind.Smoke => R(1.2f, 2.2f), Kind.Spark => R(0.12f, 0.25f), _ => R(0.3f, 0.55f) },
                Size = size * R(0.7f, 1.2f),
                Grow = kind == Kind.Smoke ? 0.8f : 0.4f,
                Bright = kind == Kind.Spark,
            });
    }

    public void Flash(Vector3 at, float size = 0.5f) =>
        bits.Add(new Bit { Kind = Kind.Flash, At = at, Life = 0.06f, Size = size, Bright = true, Rot = R(0, 360) });

    /// <summary>RimWorld's explosion, as its damage type looks: a fireball, an EMP's crackle,
    /// smoke, foam, a stun's flash.</summary>
    public void Explosion(Explosion e, Vector3 eye)
    {
        var at = new Vector3(e.X, 0.2f, e.Z);
        float r = Math.Clamp(e.Radius, 0.8f, 8f);
        string d = e.Damage;
        if (d.Contains("Smoke") || d.Contains("Tox") || d.Contains("Rot") || d.Contains("Dust"))
        {
            for (int i = 0; i < 14; i++)
                bits.Add(new Bit { Kind = Kind.Smoke, At = at + new Vector3(R(-r, r) * 0.6f, R(0.1f, 1f), R(-r, r) * 0.6f), Vel = new Vector3(R(-0.3f, 0.3f), R(0.05f, 0.25f), R(-0.3f, 0.3f)), Life = R(4f, 7f), Size = r * 0.7f, Grow = 0.3f, Color = d.Contains("Tox") ? new Color(150, 220, 110, 255) : Color.White });
            return;
        }
        if (d.Contains("Extinguish") || d.Contains("Foam"))
        {
            for (int i = 0; i < 12; i++)
                bits.Add(new Bit { Kind = Kind.Foam, At = at + new Vector3(R(-r, r) * 0.6f, R(0, 0.6f), R(-r, r) * 0.6f), Vel = new Vector3(0, R(-0.2f, 0.1f), 0), Life = R(1.5f, 3f), Size = r * 0.6f, Grow = 0.2f });
            return;
        }
        Kind kind = d.Contains("EMP") ? Kind.Emp : Kind.Boom;
        bits.Add(new Bit { Kind = kind, At = at + new Vector3(0, r * 0.6f, 0), Life = 0.6f, Size = r * 2.2f, Bright = true });
        if (kind == Kind.Boom)
        {
            for (int i = 0; i < 6; i++)
                bits.Add(new Bit { Kind = Kind.Smoke, At = at + new Vector3(R(-r, r) * 0.4f, R(0.3f, 1.2f), R(-r, r) * 0.4f), Vel = new Vector3(R(-0.3f, 0.3f), R(0.4f, 0.9f), R(-0.3f, 0.3f)), Life = R(1.5f, 3f), Size = r * 0.6f, Grow = 0.5f, Age = -0.25f });
            for (int i = 0; i < 16; i++)
                bits.Add(new Bit { Kind = Kind.Spark, At = at + new Vector3(0, 0.3f, 0), Vel = new Vector3(R(-6, 6), R(2, 7), R(-6, 6)), Life = R(0.3f, 0.7f), Size = 0.12f, Bright = true });
        }
        float distance = Vector3.Distance(at, new Vector3(eye.X, eye.Y, -eye.Z));
        float near = Math.Clamp(1f - distance / (r * 6f + 6f), 0f, 1f);
        Shake = Math.Max(Shake, near);
        Blast = Math.Max(Blast, near * 0.5f);
    }

    /// <summary>RimWorld's flecks (dust, smoke puffs, sparks, footprints): each moves, grows,
    /// turns and fades as RimWorld would have it.</summary>
    public void Fleck(FleckState f, Pic pic)
    {
        if (bits.Count > 1500) return;
        Color color = Color.White;
        if (f.Color is { Length: >= 3 } c)
            color = new Color((byte)(c[0] * 255), (byte)(c[1] * 255), (byte)(c[2] * 255), (byte)(255 * (c.Length > 3 ? c[3] : 1)));
        bits.Add(new Bit
        {
            Kind = Kind.Fleck,
            Pic = pic,
            At = new Vector3(f.X, f.Flat ? 0.02f : Math.Max(0.1f, f.Y + 0.1f), f.Z),
            Vel = new Vector3(f.Vx, f.Vy, f.Vz),
            Size = Math.Max(0.05f, f.Size),
            Grow = f.Grow,
            Rot = f.Rot,
            Spin = f.Spin,
            FadeIn = f.FadeIn,
            Solid = f.Solid,
            FadeOut = f.FadeOut,
            Life = Math.Max(0.05f, f.FadeIn + f.Solid + f.FadeOut),
            Flat = f.Flat,
            Color = color,
        });
    }

    public void Update(float dt, float gameDt)
    {
        time += dt;
        Shake = Math.Max(0, Shake - dt * 2.2f);
        Blast = Math.Max(0, Blast - dt * 2.5f);
        foreach (Bit b in bits)
        {
            float step = b.Kind == Kind.Fleck ? gameDt : dt;
            b.Age += step;
            if (b.Age < 0) continue;
            b.At += b.Vel * step;
            if (b.Kind == Kind.Spark) b.Vel.Y -= 12f * step;
            if (b.Kind is Kind.Puff or Kind.Blood or Kind.Grit) b.Vel *= MathF.Max(0, 1 - step * 3);
            b.Size += b.Grow * step;
            b.Rot += b.Spin * step;
            if (b.At.Y < 0.01f && b.Kind == Kind.Spark) b.Vel.Y = MathF.Abs(b.Vel.Y) * 0.3f;
        }
        bits.RemoveAll(b => b.Age >= b.Life);
    }

    private float Fade(Bit b)
    {
        if (b.Kind == Kind.Fleck)
        {
            if (b.Age < b.FadeIn) return b.FadeIn > 0 ? b.Age / b.FadeIn : 1;
            float outFrom = b.FadeIn + b.Solid;
            return b.Age < outFrom ? 1 : b.FadeOut > 0 ? 1 - (b.Age - outFrom) / b.FadeOut : 0;
        }
        float t = b.Age / b.Life;
        return b.Kind is Kind.Smoke or Kind.Foam ? MathF.Min(1, (1 - t) * 1.6f) * 0.85f : 1 - t * t;
    }

    public void Draw(Sprites sprites, Builtins art, Func<float, float, float, Color> lit)
    {
        foreach (Bit b in bits)
        {
            if (b.Age < 0) continue;
            float fade = Math.Clamp(Fade(b), 0, 1);
            Vector3 at = WorldMesh.P(b.At.X, b.At.Y, b.At.Z);
            Color light = b.Bright ? Color.White : lit(b.At.X, b.At.Z, b.At.Y);
            Color Tint(Color c) => new((byte)(light.R * c.R / 255), (byte)(light.G * c.G / 255), (byte)(light.B * c.B / 255), (byte)(255 * fade * c.A / 255));
            float t = b.Age / b.Life;
            switch (b.Kind)
            {
                case Kind.Flash:
                    sprites.Spin(art.Flash[(int)(b.Rot) % 2], at, b.Size, b.Size, b.Rot, Color.White, bright: true);
                    break;
                case Kind.Boom:
                    {
                        Pic frame = art.Boom[Math.Clamp((int)(t * art.Boom.Length), 0, art.Boom.Length - 1)];
                        sprites.Spin(frame, at, b.Size, b.Size, 0, Color.White, bright: true, cut: false);
                        break;
                    }
                case Kind.Emp:
                    {
                        Pic frame = art.Emp[(int)(time * 20) % art.Emp.Length];
                        sprites.Spin(frame, at, b.Size, b.Size, 0, new Color(255, 255, 255, (int)(255 * fade)), bright: true, cut: false);
                        break;
                    }
                case Kind.Spark:
                    sprites.Spin(art.Spark, at, b.Size, b.Size, 0, Color.White, bright: true, cut: false);
                    break;
                case Kind.Fleck when b.Pic != null:
                    {
                        Pic pic = b.Pic;
                        float w = b.Size, h = b.Size * pic.R.H / Math.Max(1, pic.R.W);
                        if (b.Flat) sprites.Flat(pic, at, w, h, b.Rot, Tint(b.Color));
                        else sprites.Spin(pic, at, w, h, b.Rot, Tint(b.Color), cut: false);
                        break;
                    }
                default:
                    {
                        Pic pic = b.Kind switch
                        {
                            Kind.Smoke => art.Smoke,
                            Kind.Blood => art.Blood,
                            Kind.Grit => art.Grit,
                            Kind.Foam => art.Foam,
                            _ => art.Dust,
                        };
                        sprites.Spin(pic, at, b.Size, b.Size, b.Rot, Tint(b.Color), cut: false);
                        break;
                    }
            }
        }
    }

    /// <summary>RimWorld's fires: flickering flames, as big as RimWorld's fire is, fullbright.</summary>
    public void Fires(Sprites sprites, Builtins art, List<Fire> fires, Vector3 eye)
    {
        int i = 0;
        foreach (Fire f in fires)
        {
            i++;
            float size = Math.Clamp(f.Size, 0.2f, 1.6f);
            Pic frame = art.Fire[(int)(time * 10 + i * 1.7f) % art.Fire.Length];
            float h = 0.4f + size * 0.9f, w = h * 0.75f;
            sprites.Stand(frame, WorldMesh.P(f.X, 0, f.Z), w, h, Color.White, flip: i % 2 == 0, bright: true);
            if (size > 0.9f && (int)(time * 4 + i) % 7 == 0 && random.NextDouble() < 0.1)
                Puff(new Vector3(f.X, h, f.Z), Kind.Smoke, 0.6f, 1);
        }
    }

    /// <summary>RimWorld's gas, a dithered cloud a 2 by 2 cell patch, thicker the denser.</summary>
    public void Gas(Sprites sprites, Builtins art, List<Gas> gas, Func<float, float, float, Color> lit)
    {
        foreach (Gas g in gas)
        {
            Pic pic = g.Kind switch { "tox" => art.Tox, "rot" => art.Rot, "dust" => art.Dust, _ => art.Smoke };
            float density = Math.Clamp(g.Density / 255f, 0.15f, 1f);
            for (int k = 0; k < 2; k++)
            {
                float x = g.X + 1f + MathF.Sin(time * 0.3f + g.X + k * 2.1f) * 0.4f;
                float z = g.Z + 1f + MathF.Cos(time * 0.25f + g.Z + k * 1.3f) * 0.4f;
                float y = 0.5f + k * 0.8f;
                Color light = lit(x, z, y);
                sprites.Spin(pic, WorldMesh.P(x, y, z), 2.2f, 2.2f, time * 8f + k * 90f,
                    new Color(light.R, light.G, light.B, (byte)(200 * density)), cut: false);
            }
        }
    }

    /// <summary>Filth as RimWorld prints it, flat on the ground, see-through where its picture is.</summary>
    public static void Filth(Sprites sprites, Pictures pictures, List<Filth> filth, Vector3 eye, Grid grid, Func<float, float, float, Color> lit)
    {
        float ex = eye.X, ez = -eye.Z;
        foreach (Filth f in filth)
        {
            if (MathF.Abs(f.X - ex) > 40 || MathF.Abs(f.Z - ez) > 40) continue;
            Pic? pic = pictures.Get(f.Tex);
            if (pic == null) continue;
            float lift = 0.012f;
            int cx = (int)MathF.Floor(f.X), cz = (int)MathF.Floor(f.Z);
            if (grid.Inside(cx, cz) && grid.Codes[grid.Index(cx, cz)] is Code.Shallow) lift = -0.03f;
            sprites.Flat(pic, WorldMesh.P(f.X, lift, f.Z), f.Size, f.Size, f.Rot, lit(f.X, f.Z, 0f));
        }
    }

    /// <summary>Rain and snow around the player, falling as hard as RimWorld says, slanted
    /// by its wind, but not under a roof.</summary>
    public void Weather(Sprites sprites, Builtins art, Weather w, Vector3 eye, Grid grid, float sky)
    {
        float rain = Math.Clamp(w.Rain, 0, 2), snow = Math.Clamp(w.Snow, 0, 2);
        if (rain < 0.05f && snow < 0.05f) return;
        float ex = eye.X, ez = -eye.Z;
        int drops = (int)(rain * 500), flakes = (int)(snow * 450);
        byte level = (byte)(90 + 140 * Math.Clamp(sky, 0, 1));
        var color = new Color(level, level, (byte)Math.Min(255, level + 20), (byte)255);
        const float reach = 16f, top = 10f;
        for (int i = 0; i < drops + flakes; i++)
        {
            bool isRain = i < drops;
            float speed = isRain ? 18f : 1.6f;
            float px = PixelArt.Rand(i, 1, 9) * reach * 2 - reach, pz = PixelArt.Rand(i, 2, 9) * reach * 2 - reach;
            // A grid fixed to the world, so drops do not slide as you walk.
            float x = ex + (((px - ex) % (reach * 2)) + reach * 3) % (reach * 2) - reach;
            float z = ez + (((pz - ez) % (reach * 2)) + reach * 3) % (reach * 2) - reach;
            float y = top - ((time * speed + PixelArt.Rand(i, 3, 9) * top) % top);
            int cx = (int)MathF.Floor(x), cz = (int)MathF.Floor(z);
            if (!grid.Inside(cx, cz) || grid.RoofAt(cx, cz) != (byte)'.') continue;
            float slant = w.Wind * 0.15f;
            if (isRain)
                sprites.Streak(art.Rain, WorldMesh.P(x, y, z), WorldMesh.P(x + slant * 0.4f, y + 0.4f, z), 0.025f, eye, color, bright: false);
            else
            {
                float drift = MathF.Sin(time * 1.3f + i) * 0.3f;
                sprites.Spin(art.Snow, WorldMesh.P(x + drift + slant * y, y, z), 0.07f, 0.07f, 0, color, cut: false);
            }
        }
    }
}
