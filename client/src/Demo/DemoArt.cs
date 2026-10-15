using RimFps.Render;
using Raylib_cs;

namespace RimFps.Demo;

/// <summary>
/// Stand-in art for the demo, painted in RimWorld's manner (flat colours, dark outlines,
/// seen from above at a slant) and saved as PNG files the way the mod saves RimWorld's own,
/// so the demo goes through exactly the client's real loading path. None of it is RimWorld's.
/// </summary>
public sealed class DemoArt
{
    public readonly string Dir;
    public readonly List<string> Names = new();
    private readonly Dictionary<string, int> index = new();

    private static readonly Color Ink = new(28, 24, 22, 255);
    private static readonly Color Clear = new(0, 0, 0, 0);

    public DemoArt()
    {
        Dir = Path.Combine(Path.GetTempPath(), "rimfps-demo-art");
        Directory.CreateDirectory(Dir);
    }

    public int this[string name] => index.TryGetValue(name, out int i) ? i : -1;

    private int Save(string name, Canvas canvas)
    {
        if (index.TryGetValue(name, out int i)) return i;
        canvas.Save(Path.Combine(Dir, name + ".png"));
        i = Names.Count;
        Names.Add(name);
        index[name] = i;
        return i;
    }

    public void PaintAll()
    {
        Wall("Wall_Atlas_Bricks_8A8A8A", new Color(150, 146, 140, 255), new Color(118, 104, 92, 255), bricks: true);
        Wall("Wall_Atlas_Planks_8B6B4A", new Color(150, 116, 78, 255), new Color(112, 82, 52, 255), bricks: false);
        Floor("WoodFloor_6B4D2E_floor", new Color(132, 98, 64, 255), Planks);
        Floor("TileStone_9A9A9A_floor", new Color(150, 148, 140, 255), Tiles);
        Floor("Soil_7A6146_floor", new Color(116, 94, 66, 255), Dirt);
        Floor("SoilRich_5E4A36_floor", new Color(88, 70, 50, 255), Dirt);
        Floor("Sand_C8B48C_floor", new Color(200, 180, 138, 255), Dirt);
        Floor("Gravel_8C8678_floor", new Color(140, 134, 120, 255), Pebbles);
        Floor("RoughStone_7E7A74_floor", new Color(126, 122, 116, 255), Pebbles);
        Save("DoorSimple_8B6B4A", Door());
        Save("Bed_8D6A42", Bed());
        Save("Table2x2_8D6A42", Table(128, 128));
        Save("DiningChair_8D6A42", Chair());
        Save("Shelf_8D6A42", Shelf());
        Save("Sandbags_A89A78", Sandbags());
        Save("MiniTurret_9C9C9C", Turret());
        Save("StandingLamp_cut", Lamp());
        Save("Dresser_8D6A42", Table(128, 64));
        Save("Gun_AssaultRifle_cut", Rifle());
        Save("Gun_Revolver_cut", Revolver());
        Save("MeleeWeapon_Knife_cut", Blade(60));
        Save("MeleeWeapon_LongSword_cut", Blade(108));
        Save("Weapon_GrenadeFrag_cut", Grenade());
        Save("Proj_GrenadeFrag_cut", Grenade());
        Save("TreeOak_cut", Tree(new Color(70, 122, 52, 255), false));
        Save("TreePine_cut", Tree(new Color(40, 92, 58, 255), true));
        Save("Bush_cut", Bush());
        Save("Grass_cut", Grass());
        Save("Corn_cut", Crop());
        Save("Steel_cut", Steel());
        Save("Medicine_cut", Medicine());
        Save("MealSimple_cut", Meal());
        Save("WoodLog_cut", Logs());
        Save("FilthBlood_cut", Blood());
        Save("Mote_Dust_cut", Dust());
        foreach (var (key, skin, hair, shirt, pants, helmet) in People)
        {
            Save($"pawn_{key}_0_cut", Person(0, skin, hair, shirt, pants, helmet));
            Save($"pawn_{key}_1_cut", Person(1, skin, hair, shirt, pants, helmet));
            Save($"pawn_{key}_2_cut", Person(2, skin, hair, shirt, pants, helmet));
        }
        Save("Muffalo_north_cut", Muffalo(0));
        Save("Muffalo_east_cut", Muffalo(1));
        Save("Muffalo_south_cut", Muffalo(2));
    }

    public static readonly (string key, Color skin, Color hair, Color shirt, Color pants, Color? helmet)[] People =
    {
        ("ada", new Color(232, 190, 156, 255), new Color(170, 72, 40, 255), new Color(176, 52, 46, 255), new Color(70, 76, 96, 255), null),
        ("bryn", new Color(150, 104, 74, 255), new Color(40, 30, 24, 255), new Color(64, 104, 168, 255), new Color(86, 70, 52, 255), null),
        ("grim", new Color(220, 176, 140, 255), new Color(60, 52, 46, 255), new Color(110, 90, 62, 255), new Color(70, 60, 48, 255), new Color(96, 96, 92, 255)),
        ("vex", new Color(196, 150, 116, 255), new Color(220, 210, 180, 255), new Color(90, 110, 70, 255), new Color(60, 66, 50, 255), null),
        ("kael", new Color(120, 84, 60, 255), new Color(24, 20, 18, 255), new Color(140, 50, 50, 255), new Color(50, 44, 40, 255), null),
        ("rook", new Color(236, 200, 170, 255), new Color(120, 90, 50, 255), new Color(130, 130, 136, 255), new Color(80, 80, 86, 255), new Color(150, 150, 156, 255)),
        ("tess", new Color(210, 168, 130, 255), new Color(200, 140, 60, 255), new Color(80, 70, 110, 255), new Color(56, 52, 70, 255), null),
    };

    private void Wall(string key, Color top, Color face, bool bricks)
    {
        Canvas Piece(bool open)
        {
            var c = new Canvas(128, 128);
            c.Fill(top);
            // Blocks seen from above.
            for (int y = 0; y < 128; y += 32)
                for (int x = (y / 32 % 2) * 16; x < 128 + 32; x += 32)
                {
                    c.Rect(x - 32, y, 30, 1, PixelArt.Shade(top, 1.18f));
                    c.Rect(x - 32, y + 30, 32, 2, PixelArt.Shade(top, 0.8f));
                    c.Rect(x - 2, y, 2, 32, PixelArt.Shade(top, 0.8f));
                }
            c.Grain(0.25f, bricks ? 3 : 4);
            if (!open) return c;
            // The front face along the bottom, as RimWorld draws a wall open to the south.
            for (int y = 92; y < 124; y++)
                for (int x = 0; x < 128; x++)
                {
                    bool mortar = bricks ? (y - 92) % 8 == 7 || (x + ((y - 92) / 8 % 2) * 8) % 16 == 15 : x % 12 == 11;
                    c.Set(x, y, mortar ? PixelArt.Shade(face, 0.6f) : PixelArt.Shade(face, 0.92f + PixelArt.Rand(x / 4, y / 4, 9) * 0.16f));
                }
            c.Rect(0, 124, 128, 4, PixelArt.Shade(face, 0.35f));
            return c;
        }
        Save(key + "_l15", Piece(false));
        Save(key + "_l11", Piece(true));
    }

    private delegate void Pattern(Canvas c, Color basis, int seed);

    private void Floor(string name, Color basis, Pattern pattern)
    {
        var c = new Canvas(512, 512);
        c.Fill(basis);
        pattern(c, basis, name.Length);
        Save(name, c);
    }

    private static void Planks(Canvas c, Color basis, int seed)
    {
        for (int x = 0; x < 512; x += 16)
        {
            int offset = (int)(PixelArt.Rand(x, 0, seed) * 96);
            float tone = 0.86f + PixelArt.Rand(x, 1, seed) * 0.24f;
            for (int y = 0; y < 512; y++)
            {
                bool seam = x % 16 == 0 || (y + offset) % 96 == 0;
                float grain = PixelArt.Noise(x * 0.3f, y * 0.05f, 64, seed + x) * 0.18f;
                c.Set(x, y, PixelArt.Shade(basis, seam ? 0.55f : tone + grain));
                for (int i = 1; i < 16; i++)
                    c.Set(x + i, y, PixelArt.Shade(basis, (y + offset) % 96 == 0 ? 0.55f : tone + grain + (i == 1 ? 0.1f : 0)));
            }
        }
    }

    private static void Tiles(Canvas c, Color basis, int seed)
    {
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                bool grout = x % 32 < 2 || y % 32 < 2;
                float tone = 0.9f + PixelArt.Rand(x / 32, y / 32, seed) * 0.15f + PixelArt.Fbm(x, y, 512, 16, seed) * 0.1f;
                c.Set(x, y, PixelArt.Shade(basis, grout ? 0.62f : tone));
            }
    }

    private static void Dirt(Canvas c, Color basis, int seed)
    {
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                float n = PixelArt.Fbm(x, y, 512, 16, seed);
                float speck = PixelArt.Rand(x, y, seed);
                c.Set(x, y, PixelArt.Shade(basis, 0.8f + n * 0.4f + (speck > 0.94f ? 0.15f : speck < 0.05f ? -0.15f : 0)));
            }
    }

    private static void Pebbles(Canvas c, Color basis, int seed)
    {
        Dirt(c, basis, seed);
        var random = new Random(seed);
        for (int i = 0; i < 900; i++)
        {
            int x = random.Next(512), y = random.Next(512), r = random.Next(2, 6);
            Color stone = PixelArt.Shade(basis, 0.8f + (float)random.NextDouble() * 0.5f);
            c.Ellipse(x, y, r, r * 0.8f, stone);
            c.Ellipse(x - 1, y - 1, r * 0.4f, r * 0.3f, PixelArt.Shade(stone, 1.2f));
        }
    }

    private static Canvas Door()
    {
        var c = new Canvas(128, 128);
        var wood = new Color(140, 106, 72, 255);
        c.Rect(0, 52, 128, 24, PixelArt.Shade(wood, 0.7f));
        c.Rect(4, 56, 58, 16, wood);
        c.Rect(66, 56, 58, 16, wood);
        c.Grain(0.3f, 8);
        return c;
    }

    private static Canvas Bed()
    {
        var c = new Canvas(128, 128);
        var frame = new Color(140, 106, 68, 255);
        c.Rect(30, 6, 68, 118, frame);
        c.Rect(34, 10, 60, 30, new Color(232, 228, 214, 255));
        c.Rect(34, 42, 60, 78, new Color(70, 104, 150, 255));
        c.Rect(34, 42, 60, 6, new Color(220, 220, 210, 255));
        c.Grain(0.2f, 11);
        c.Outline(Ink, 1);
        return c;
    }

    private static Canvas Table(int w, int h)
    {
        var c = new Canvas(128, 128);
        var wood = new Color(150, 112, 70, 255);
        c.Rect((128 - w) / 2 + 6, (128 - h) / 2 + 6, w - 12, h - 12, wood);
        for (int y = (128 - h) / 2 + 6; y < (128 + h) / 2 - 6; y += 10) c.Rect((128 - w) / 2 + 6, y, w - 12, 1, PixelArt.Shade(wood, 0.75f));
        c.Grain(0.2f, 12);
        c.Outline(Ink, 1);
        return c;
    }

    private static Canvas Chair()
    {
        var c = new Canvas(128, 128);
        var wood = new Color(150, 112, 70, 255);
        c.Rect(38, 40, 52, 52, wood);
        c.Rect(38, 30, 52, 12, PixelArt.Shade(wood, 0.8f));
        c.Outline(Ink, 1);
        return c;
    }

    private static Canvas Shelf()
    {
        var c = new Canvas(128, 128);
        var wood = new Color(130, 96, 60, 255);
        c.Rect(8, 30, 112, 68, wood);
        for (int x = 14; x < 118; x += 26) c.Rect(x, 38, 20, 52, new Color(176, 150, 100, 255));
        c.Grain(0.25f, 13);
        c.Outline(Ink, 1);
        return c;
    }

    private static Canvas Sandbags()
    {
        var c = new Canvas(128, 128);
        var sack = new Color(176, 158, 116, 255);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4; i++)
                c.Ellipse(18 + i * 31 + (row % 2) * 14, 36 + row * 26, 18, 13, PixelArt.Shade(sack, 0.9f + row * 0.06f));
        c.Grain(0.25f, 14);
        c.Outline(Ink, 1);
        return c;
    }

    private static Canvas Turret()
    {
        var c = new Canvas(128, 128);
        c.Ellipse(64, 64, 44, 44, new Color(110, 112, 116, 255));
        c.Ellipse(64, 64, 26, 26, new Color(80, 82, 86, 255));
        c.Rect(60, 10, 8, 54, new Color(60, 62, 66, 255));
        c.Grain(0.2f, 15);
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Lamp()
    {
        var c = new Canvas(128, 128);
        c.Rect(60, 40, 8, 80, new Color(80, 80, 84, 255));
        c.Rect(46, 116, 36, 8, new Color(70, 70, 74, 255));
        c.Ellipse(64, 34, 26, 22, new Color(250, 236, 170, 255));
        c.Ellipse(64, 30, 16, 12, new Color(255, 252, 226, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Rifle()
    {
        var c = new Canvas(128, 128);
        var metal = new Color(76, 78, 82, 255);
        var wood = new Color(120, 86, 54, 255);
        c.Rect(10, 58, 34, 12, wood);       // stock
        c.Rect(40, 54, 50, 14, metal);      // body
        c.Rect(88, 58, 34, 5, metal);       // barrel
        c.Rect(54, 66, 9, 18, metal);       // magazine
        c.Rect(70, 66, 6, 12, wood);        // grip
        c.Rect(56, 50, 20, 4, PixelArt.Shade(metal, 0.7f)); // sight
        c.Grain(0.15f, 16);
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Revolver()
    {
        var c = new Canvas(128, 128);
        var metal = new Color(150, 150, 156, 255);
        c.Rect(46, 54, 44, 9, metal);
        c.Ellipse(56, 62, 10, 9, PixelArt.Shade(metal, 0.85f));
        c.Rect(40, 60, 12, 26, new Color(110, 70, 44, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Blade(int length)
    {
        var c = new Canvas(128, 128);
        int x0 = (128 - length) / 2;
        c.Rect(x0, 60, 18, 8, new Color(90, 60, 40, 255));
        c.Rect(x0 + 16, 54, 5, 20, new Color(160, 140, 80, 255));
        c.Rect(x0 + 21, 59, length - 25, 10, new Color(200, 204, 210, 255));
        c.Rect(x0 + 21, 59, length - 25, 2, new Color(240, 244, 250, 255));
        c.Line(x0 + length - 6, 59, x0 + length, 64, 3, new Color(200, 204, 210, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Grenade()
    {
        var c = new Canvas(128, 128);
        c.Ellipse(64, 68, 24, 28, new Color(80, 100, 60, 255));
        for (int y = 48; y < 96; y += 9) c.Rect(42, y, 44, 2, new Color(60, 76, 44, 255));
        c.Rect(56, 36, 16, 10, new Color(120, 120, 124, 255));
        c.Rect(70, 34, 12, 4, new Color(150, 150, 154, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Tree(Color leaf, bool pine)
    {
        var c = new Canvas(128, 128);
        c.Rect(58, 70, 12, 56, new Color(100, 72, 46, 255));
        if (pine)
            for (int i = 0; i < 5; i++)
            {
                int w = 50 - i * 8, y = 92 - i * 18;
                for (int row = 0; row < 26; row++)
                    c.Rect(64 - w * row / 26, y - 26 + row, w * row / 26 * 2, 1, PixelArt.Shade(leaf, 0.85f + row / 120f));
            }
        else
        {
            c.Ellipse(64, 46, 46, 40, leaf);
            c.Ellipse(40, 58, 26, 22, PixelArt.Shade(leaf, 0.9f));
            c.Ellipse(88, 56, 26, 22, PixelArt.Shade(leaf, 0.92f));
            c.Ellipse(56, 30, 22, 16, PixelArt.Shade(leaf, 1.15f));
        }
        c.Grain(0.35f, pine ? 17 : 18);
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Bush()
    {
        var c = new Canvas(128, 128);
        var leaf = new Color(78, 120, 54, 255);
        c.Ellipse(64, 86, 44, 34, leaf);
        c.Ellipse(44, 92, 24, 20, PixelArt.Shade(leaf, 0.88f));
        c.Ellipse(86, 90, 24, 22, PixelArt.Shade(leaf, 1.1f));
        for (int i = 0; i < 8; i++) c.Ellipse(40 + i * 7, 76 + (i % 3) * 8, 3, 3, new Color(200, 40, 60, 255));
        c.Grain(0.35f, 19);
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Grass()
    {
        var c = new Canvas(128, 128);
        var blade = new Color(108, 150, 62, 255);
        var random = new Random(20);
        for (int i = 0; i < 16; i++)
        {
            float x = 28 + random.Next(72), lean = (float)random.NextDouble() * 24 - 12;
            c.Line(x, 124, x + lean, 60 + random.Next(30), 4, PixelArt.Shade(blade, 0.8f + (float)random.NextDouble() * 0.4f));
        }
        c.Outline(new Color(40, 60, 24, 255), 1);
        return c;
    }

    private static Canvas Crop()
    {
        var c = new Canvas(128, 128);
        c.Rect(60, 30, 8, 94, new Color(110, 150, 60, 255));
        for (int i = 0; i < 4; i++)
        {
            c.Line(64, 110 - i * 20, 34 + (i % 2) * 60, 92 - i * 20, 6, new Color(96, 140, 56, 255));
        }
        c.Ellipse(70, 50, 8, 18, new Color(236, 204, 80, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Steel()
    {
        var c = new Canvas(128, 128);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 3 - row; i++)
            {
                int x = 30 + i * 24 + row * 12, y = 84 - row * 16;
                c.Rect(x, y, 22, 14, new Color(150, 156, 166, 255));
                c.Rect(x, y, 22, 3, new Color(196, 202, 210, 255));
            }
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Medicine()
    {
        var c = new Canvas(128, 128);
        c.Rect(36, 50, 56, 44, new Color(236, 236, 230, 255));
        c.Rect(58, 56, 12, 32, new Color(200, 40, 40, 255));
        c.Rect(48, 66, 32, 12, new Color(200, 40, 40, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Meal()
    {
        var c = new Canvas(128, 128);
        c.Ellipse(64, 72, 38, 24, new Color(230, 230, 224, 255));
        c.Ellipse(64, 68, 26, 14, new Color(190, 140, 70, 255));
        c.Ellipse(56, 64, 8, 5, new Color(90, 150, 60, 255));
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Logs()
    {
        var c = new Canvas(128, 128);
        for (int i = 0; i < 3; i++)
        {
            int y = 56 + i * 14 - (i == 2 ? 26 : 0), x = i == 2 ? 46 : 26 + i * 4;
            c.Rect(x, y, 66, 13, new Color(126, 88, 54, 255));
            c.Ellipse(x + 66, y + 6.5f, 6, 6.5f, new Color(196, 160, 110, 255));
        }
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Blood()
    {
        var c = new Canvas(128, 128);
        var red = new Color(130, 16, 14, 255);
        c.Ellipse(64, 64, 34, 26, red);
        c.Ellipse(40, 50, 14, 10, red);
        c.Ellipse(90, 80, 12, 9, red);
        c.Ellipse(80, 40, 8, 6, red);
        c.Grain(0.3f, 21);
        return c;
    }

    private static Canvas Dust()
    {
        var c = new Canvas(128, 128);
        c.Ellipse(64, 64, 50, 40, new Color(170, 150, 120, 255));
        c.Ellipse(50, 56, 30, 24, new Color(186, 168, 138, 255));
        return c;
    }

    /// <summary>A person as RimWorld draws one: a round head over a body, facing north
    /// (0, their back), east (1) or south (2, their face).</summary>
    private static Canvas Person(int facing, Color skin, Color hair, Color shirt, Color pants, Color? helmet)
    {
        var c = new Canvas(128, 128);
        bool side = facing == 1;
        float bodyW = side ? 20 : 27;
        // Legs, body and arms.
        c.Rect((int)(64 - bodyW + 5), 92, (int)(bodyW * 2 - 10), 22, pants);
        c.Ellipse(64, 80, bodyW, 24, shirt);
        if (!side)
        {
            c.Ellipse(64 - bodyW - 2, 82, 7, 16, PixelArt.Shade(shirt, 0.88f));
            c.Ellipse(64 + bodyW + 2, 82, 7, 16, PixelArt.Shade(shirt, 0.88f));
            c.Ellipse(64 - bodyW - 2, 98, 6, 5, skin);
            c.Ellipse(64 + bodyW + 2, 98, 6, 5, skin);
        }
        else
        {
            c.Ellipse(70, 84, 8, 15, PixelArt.Shade(shirt, 0.85f));
            c.Ellipse(73, 99, 6, 5, skin);
        }
        // Head.
        float hx = side ? 68 : 64;
        c.Ellipse(hx, 42, 26, 25, skin);
        if (facing == 0) c.Ellipse(hx, 40, 26, 24, hair);
        else
        {
            // Hair over the top, and the face.
            for (int y = 14; y < 32; y++)
                for (int x = (int)hx - 27; x <= hx + 27; x++)
                {
                    float dx = (x - hx) / 26f, dy = (y - 42) / 25f;
                    if (dx * dx + dy * dy <= 1f) c.Set(x, y, hair);
                }
            if (side)
            {
                c.Ellipse(hx - 14, 36, 13, 18, hair);
                c.Ellipse(hx + 14, 44, 3, 3, Ink);
            }
            else
            {
                c.Ellipse(hx - 9, 45, 3, 3.5f, Ink);
                c.Ellipse(hx + 9, 45, 3, 3.5f, Ink);
                c.Rect((int)hx - 5, 56, 10, 2, PixelArt.Shade(skin, 0.7f));
            }
        }
        if (helmet is Color h)
        {
            for (int y = 12; y < 36; y++)
                for (int x = (int)hx - 28; x <= hx + 28; x++)
                {
                    float dx = (x - hx) / 28f, dy = (y - 40) / 28f;
                    if (dx * dx + dy * dy <= 1f) c.Set(x, y, h);
                }
            c.Rect((int)hx - 28, 34, 56, 3, PixelArt.Shade(h, 0.7f));
        }
        c.Grain(0.12f, facing + skin.R);
        c.Outline(Ink, 2);
        return c;
    }

    private static Canvas Muffalo(int facing)
    {
        var c = new Canvas(128, 128);
        var wool = new Color(150, 116, 84, 255);
        bool side = facing == 1;
        if (side)
        {
            c.Ellipse(58, 70, 44, 32, wool);
            c.Rect(26, 92, 10, 26, PixelArt.Shade(wool, 0.6f));
            c.Rect(80, 92, 10, 26, PixelArt.Shade(wool, 0.6f));
            c.Ellipse(104, 62, 16, 16, PixelArt.Shade(wool, 0.8f));
            c.Ellipse(110, 64, 4, 4, Ink);
            c.Line(96, 50, 88, 38, 5, new Color(220, 210, 190, 255));
        }
        else
        {
            c.Ellipse(64, 70, 40, 36, wool);
            c.Rect(36, 96, 12, 24, PixelArt.Shade(wool, 0.6f));
            c.Rect(80, 96, 12, 24, PixelArt.Shade(wool, 0.6f));
            if (facing == 2)
            {
                c.Ellipse(64, 72, 16, 18, PixelArt.Shade(wool, 0.8f));
                c.Ellipse(57, 70, 3, 3, Ink);
                c.Ellipse(71, 70, 3, 3, Ink);
                c.Line(48, 58, 36, 44, 5, new Color(220, 210, 190, 255));
                c.Line(80, 58, 92, 44, 5, new Color(220, 210, 190, 255));
            }
        }
        c.Grain(0.4f, 22 + facing);
        c.Outline(Ink, 2);
        return c;
    }
}
