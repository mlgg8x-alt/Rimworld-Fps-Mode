using RimFps.Render;
using Raylib_cs;

namespace RimFps.World;

/// <summary>The client's own pictures, made once at start and kept in the atlas.</summary>
public sealed class Builtins
{
    public readonly Pic Rock, RockTop, Cave, Roof, Water, Deep, Soil, Sand, Stone, Floor, GrassGround, Wall, Metal, Door, White, Hand;
    public readonly Pic[] Fire, Boom, Emp;
    public readonly Pic Smoke, Dust, Tox, Rot, Foam, Blood, Grit, Tracer, Spark, Rain, Snow;
    public readonly Pic[] Flash;

    public Builtins(Pictures pictures, Settings settings)
    {
        int s = Math.Clamp(settings.PictureTexels, 16, 128);
        Pic Make(Color[] px, int w, int h, bool cutout = false) => pictures.Builtin(px, w, h, cutout);
        Rock = Make(PixelArt.Rock(s, new Color(122, 116, 108, 255), 11), s, s);
        RockTop = Make(PixelArt.Rock(s, new Color(136, 130, 120, 255), 12), s, s);
        Cave = Make(PixelArt.Rock(s, new Color(92, 86, 80, 255), 13), s, s);
        Roof = Make(PixelArt.Planks(s, new Color(104, 82, 60, 255), 14), s, s);
        int f = Math.Clamp(settings.FloorTexels, 8, 64);
        Water = Make(PixelArt.Water(f, false, 0), f, f);
        Deep = Make(PixelArt.Water(f, true, 0), f, f);
        Soil = Make(PixelArt.Ground(f, new Color(112, 92, 66, 255), 21), f, f);
        Sand = Make(PixelArt.Ground(f, new Color(196, 176, 132, 255), 22), f, f);
        Stone = Make(PixelArt.Rock(f, new Color(128, 124, 116, 255), 23), f, f);
        Floor = Make(PixelArt.Planks(f, new Color(150, 120, 86, 255), 24), f, f);
        GrassGround = Make(PixelArt.Ground(f, new Color(84, 108, 52, 255), 25), f, f);
        Wall = Make(PixelArt.Planks(s, new Color(150, 146, 140, 255), 26), s, s);
        Metal = Make(PixelArt.Metal(s), s, s);
        Door = Make(PixelArt.Door(32, 48), 32, 48);
        White = Make(PixelArt.Solid(4, 4, new Color(255, 255, 255, 255)), 4, 4);
        Hand = Make(PixelArt.Hand(48), 48, 48, true);
        Fire = Enumerable.Range(0, 6).Select(i => Make(PixelArt.Flame(24, 32, i, 31), 24, 32, true)).ToArray();
        Boom = Enumerable.Range(0, 8).Select(i => Make(PixelArt.Fireball(48, i, 8, 41), 48, 48, true)).ToArray();
        Emp = Enumerable.Range(0, 4).Select(i => Make(PixelArt.Crackle(48, i), 48, 48, true)).ToArray();
        Smoke = Make(PixelArt.Puff(32, new Color(120, 120, 124, 255), 51), 32, 32, true);
        Dust = Make(PixelArt.Puff(32, new Color(150, 132, 104, 255), 52), 32, 32, true);
        Tox = Make(PixelArt.Puff(32, new Color(120, 170, 70, 255), 53), 32, 32, true);
        Rot = Make(PixelArt.Puff(32, new Color(130, 140, 90, 255), 54), 32, 32, true);
        Foam = Make(PixelArt.Puff(32, new Color(232, 236, 240, 255), 55), 32, 32, true);
        Blood = Make(PixelArt.Splat(16, new Color(150, 20, 16, 255), 61), 16, 16, true);
        Grit = Make(PixelArt.Splat(16, new Color(140, 120, 96, 255), 62), 16, 16, true);
        Tracer = Make(PixelArt.Glow(8, new Color(255, 250, 210, 255), new Color(255, 190, 60, 255)), 8, 8, true);
        Spark = Make(PixelArt.Glow(4, new Color(255, 240, 160, 255), new Color(255, 160, 40, 255)), 4, 4, true);
        Rain = Make(PixelArt.Solid(1, 6, new Color(170, 190, 220, 255)), 1, 6, true);
        Snow = Make(PixelArt.Glow(4, new Color(250, 250, 255, 255), new Color(220, 226, 240, 255)), 4, 4, true);
        Flash = Enumerable.Range(0, 2).Select(i => Make(PixelArt.MuzzleFlash(32, 71 + i), 32, 32, true)).ToArray();
    }
}
