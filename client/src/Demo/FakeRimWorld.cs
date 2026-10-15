using System.Globalization;
using System.Text;
using System.Text.Json;
using RimFps.Game;
using RimFps.Net;

namespace RimFps.Demo;

/// <summary>
/// A stand-in for RimWorld and the mod, for trying the client without the game and for its
/// tests: a made-up colony on a made-up map, raided from the north. It speaks the real
/// protocol (every message goes through <see cref="Link.Apply"/>) and answers the client's
/// messages roughly as RimWorld would: the colonist walks where you go, doors open as you
/// come up to them, raiders walk in and shoot, your hits hurt them, theirs hurt you.
/// </summary>
public sealed class FakeRimWorld
{
    private const int Size = 96;
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class Pawn
    {
        public string Id = "", Name = "", Kind = "humanlike", Faction = "hostile", Look = "";
        public string? Weapon;
        public float X, Z, Rot;
        public float Health = 1f;
        public int Hurt, Swings;
        public bool Downed, Dead;
        public float Cooldown, Aim = -1;
        public int Burst;
        public float NextBurst;
        public bool Melee, Grenades;
    }

    private readonly DemoArt art = new();
    private readonly Launch launch;
    private readonly char[] terrain = new char[Size * Size];
    private readonly char[] roofs = new char[Size * Size];
    private readonly int[] wallSkin = new int[Size * Size];
    private readonly int[] floorSkin = new int[Size * Size];
    private readonly List<string> buildings = new();
    private readonly List<(string def, int x, int z, int tex, int count)> items = new();
    private readonly List<(int x, int z, string kind, float growth)> plants = new();
    private readonly Dictionary<(int, int), float> doors = new();
    private readonly Dictionary<(int, int), float> doorHeld = new();
    private readonly List<Pawn> pawns = new();
    private readonly List<(float x, float z, float size, float left)> fires = new();
    private readonly List<(float x, float z, float rot)> filth = new();
    private readonly List<(float at, Shell shell, float x, float z)> grenades = new();
    private readonly Random random = new(1234);
    private float time, sinceSnapshot, sinceDoors, sinceFires;
    private bool started;
    private int bullet;
    private float sky;
    private Pawn Player => pawns[0];

    public FakeRimWorld(Launch launch)
    {
        this.launch = launch;
        sky = launch.Sky ?? 0.9f;
        art.PaintAll();
        Build();
    }

    private int I(int x, int z) => z * Size + x;
    private bool Inside(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size;

    private void Build()
    {
        Array.Fill(terrain, '.');
        Array.Fill(roofs, '.');
        Array.Fill(wallSkin, -1);
        int soil = art["Soil_7A6146_floor"], rich = art["SoilRich_5E4A36_floor"], sand = art["Sand_C8B48C_floor"];
        int gravel = art["Gravel_8C8678_floor"], rough = art["RoughStone_7E7A74_floor"];
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                int i = I(x, z);
                floorSkin[i] = soil;
                float n = Render.PixelArt.Fbm(x, z, Size, 8, 5);
                if (n > 0.52f) terrain[i] = 'g';
                // A mountain along the east, rising in from its edge.
                float edge = 74 + Render.PixelArt.Fbm(0, z, Size, 6, 9) * 8;
                if (x >= edge)
                {
                    terrain[i] = '#';
                    floorSkin[i] = rough;
                }
                // A pond, with sand round it.
                float pd = MathF.Sqrt((x - 18) * (x - 18) + (z - 70) * (z - 70));
                if (pd < 9.5f && terrain[i] != '#') { terrain[i] = ','; floorSkin[i] = sand; }
                if (pd < 6f) { terrain[i] = '~'; floorSkin[i] = -1; }
                if (pd < 3.5f) terrain[i] = 'O';
                // A gravel track up from the south.
                if (Math.Abs(x - 39) <= 1 && z < 30) { terrain[i] = ':'; floorSkin[i] = gravel; }
            }
        // Rock roofs over the mountain, thick deep inside; a cave runs into it.
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                if (terrain[I(x, z)] != '#') continue;
                bool deep = Enumerable.Range(-3, 7).All(d => Inside(x - 3, z + d) && terrain[I(x - 3, z)] == '#');
                roofs[I(x, z)] = deep ? 'R' : 'r';
            }
        for (int x = 72; x < 90; x++)
            for (int z = 40; z <= 44; z++)
            {
                if (z == 40 && x > 84 || z == 44 && x > 86) continue;
                terrain[I(x, z)] = ':';
                floorSkin[I(x, z)] = rough;
                if (x > 77) roofs[I(x, z)] = 'r';
            }

        // The colony: a brick house of two rooms, and a wooden workshop.
        House(30, 30, 48, 42, art["Wall_Atlas_Bricks_8A8A8A_l15"], art["WoodFloor_6B4D2E_floor"]);
        for (int z = 31; z <= 41; z++) Wall(40, z, art["Wall_Atlas_Bricks_8A8A8A_l15"]);
        Door(39, 30);
        Door(48, 36);
        Door(39, 42);
        Door(40, 36);
        House(52, 30, 58, 36, art["Wall_Atlas_Planks_8B6B4A_l15"], art["TileStone_9A9A9A_floor"]);
        Door(55, 30);

        // Furniture.
        Building("Bed", 32, 37, 32, 38, 2, art["Bed_8D6A42"], 0.6f, false, 1, 2);
        Building("Bed", 35, 37, 35, 38, 2, art["Bed_8D6A42"], 0.6f, false, 1, 2);
        Building("Dresser", 37, 40, 38, 40, 2, art["Dresser_8D6A42"], 0.5f, false, 2, 1);
        Building("StandingLamp", 38, 33, 38, 33, 0, -1, 0.2f, false, 1, 1, card: art["StandingLamp_cut"]);
        Building("Table2x2c", 43, 34, 44, 35, 0, art["Table2x2_8D6A42"], 0.5f, false, 2, 2);
        Building("DiningChair", 42, 34, 42, 34, 1, art["DiningChair_8D6A42"], 0.35f, false, 1, 1);
        Building("DiningChair", 45, 35, 45, 35, 3, art["DiningChair_8D6A42"], 0.35f, false, 1, 1);
        Building("Shelf", 46, 39, 47, 39, 2, art["Shelf_8D6A42"], 0.4f, false, 2, 1);
        Building("StandingLamp", 46, 32, 46, 32, 0, -1, 0.2f, false, 1, 1, card: art["StandingLamp_cut"]);
        Building("StandingLamp", 57, 35, 57, 35, 0, -1, 0.2f, false, 1, 1, card: art["StandingLamp_cut"]);
        Building("TableShort", 53, 33, 54, 33, 0, art["Table2x2_8D6A42"], 0.5f, false, 2, 1);
        // Sandbags across the north, facing the raid, and a turret.
        for (int x = 29; x <= 49; x++)
        {
            if (x is 38 or 39 or 40) continue;
            terrain[I(x, 49)] = 'c';
            Building("Sandbags", x, 49, x, 49, 0, art["Sandbags_A89A78"], 0.55f, false, 1, 1);
        }
        terrain[I(51, 46)] = 'M';
        Building("Turret_MiniTurret", 51, 46, 51, 46, 0, art["MiniTurret_9C9C9C"], 0.5f, true, 1, 1);

        // Things lying about.
        items.Add(("Steel", 56, 28, art["Steel_cut"], 75));
        items.Add(("WoodLog", 57, 28, art["WoodLog_cut"], 40));
        items.Add(("MedicineIndustrial", 46, 39, art["Medicine_cut"], 5));
        items.Add(("MealSimple", 47, 39, art["MealSimple_cut"], 4));
        items.Add(("MealSimple", 43, 35, art["MealSimple_cut"], 1));

        // Trees, bushes, a corn field.
        for (int k = 0; k < 260; k++)
        {
            int x = random.Next(2, 72), z = random.Next(2, Size - 2);
            if (x >= 26 && x <= 62 && z >= 24 && z <= 52) continue;
            if (Math.Abs(x - 39) <= 2 && z < 30) continue;
            int i = I(x, z);
            if (terrain[i] is not ('.' or 'g')) continue;
            if (random.NextDouble() < 0.7)
            {
                terrain[i] = 'T';
                plants.Add((x, z, random.NextDouble() < 0.5 ? "oak" : "pine", 0.4f + (float)random.NextDouble() * 0.6f));
            }
            else plants.Add((x, z, "bush", 0.6f + (float)random.NextDouble() * 0.4f));
        }
        for (int z = 12; z <= 20; z++)
            for (int x = 56; x <= 66; x++)
            {
                terrain[I(x, z)] = '.';
                floorSkin[I(x, z)] = rich;
                plants.Add((x, z, "crop_Plant_Corn", 0.5f + (float)random.NextDouble() * 0.5f));
            }

        // The people: you, a colonist inside, a muffalo, and the raid.
        pawns.Add(new Pawn { Id = "Human1", Name = "Ada", Faction = "player", Look = "ada", X = 39.5f, Z = 27.5f, Rot = 0, Weapon = "Gun_AssaultRifle" });
        pawns.Add(new Pawn { Id = "Human2", Name = "Bryn", Faction = "player", Look = "bryn", X = 44.5f, Z = 37.5f, Rot = 180, Weapon = "Gun_Revolver" });
        pawns.Add(new Pawn { Id = "Muffalo3", Name = "Muffalo", Kind = "animal", Faction = "neutral", Look = "muffalo", X = 24.5f, Z = 45.5f, Rot = 90 });
        pawns.Add(new Pawn { Id = "Human10", Name = "Grim", Look = "grim", X = 34.5f, Z = 78.5f, Rot = 180, Weapon = "Gun_AssaultRifle" });
        pawns.Add(new Pawn { Id = "Human11", Name = "Vex", Look = "vex", X = 44.5f, Z = 82.5f, Rot = 180, Weapon = "Gun_Revolver" });
        pawns.Add(new Pawn { Id = "Human12", Name = "Kael", Look = "kael", X = 39.5f, Z = 86.5f, Rot = 180, Weapon = "MeleeWeapon_Knife", Melee = true });
        pawns.Add(new Pawn { Id = "Human13", Name = "Rook", Look = "rook", X = 50.5f, Z = 80.5f, Rot = 180, Weapon = "MeleeWeapon_LongSword", Melee = true });
        pawns.Add(new Pawn { Id = "Human14", Name = "Tess", Look = "tess", X = 28.5f, Z = 84.5f, Rot = 180, Weapon = "Gun_AssaultRifle", Grenades = true });
        pawns.Add(new Pawn { Id = "Human15", Name = "Raider", Look = "grim", X = 44.5f, Z = 56.5f, Rot = 90, Dead = true, Downed = true, Weapon = "Gun_Revolver" });
        filth.Add((44.3f, 56.2f, 30));
        filth.Add((45.1f, 55.6f, 200));
        if (launch.Position is { Length: 2 } at)
        {
            Player.X = at[0];
            Player.Z = at[1];
        }
        if (launch.Burning) fires.Add((36f, 60f, 1.2f, 1e9f));
        if (launch.Weapon != null) Player.Weapon = launch.Weapon;
    }

    private void House(int x0, int z0, int x1, int z1, int wall, int floor)
    {
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = I(x, z);
                roofs[i] = 'c';
                if (x == x0 || x == x1 || z == z0 || z == z1) Wall(x, z, wall);
                else
                {
                    terrain[i] = '=';
                    floorSkin[i] = floor;
                }
            }
    }

    private void Wall(int x, int z, int skin)
    {
        terrain[I(x, z)] = 'W';
        wallSkin[I(x, z)] = skin;
    }

    private void Door(int x, int z)
    {
        terrain[I(x, z)] = '+';
        wallSkin[I(x, z)] = art["DoorSimple_8B6B4A"];
        floorSkin[I(x, z)] = art["WoodFloor_6B4D2E_floor"];
        doors[(x, z)] = 0;
    }

    private void Building(string def, int x0, int z0, int x1, int z1, int rot, int tex, float fill, bool solid, float dw, float dh, int card = -1)
    {
        float ax = (x0 + x1 + 1) / 2f, az = (z0 + z1 + 1) / 2f;
        buildings.Add($"{{\"def\":\"{def}\",\"rect\":[{x0},{z0},{x1},{z1}],\"rot\":{rot},\"at\":[{F(ax)},{F(az)}],\"draw\":[{F(dw)},{F(dh)}],\"tex\":{tex},\"turn\":0,\"flip\":false,\"fill\":{F(fill)},\"solid\":{(solid ? "true" : "false")}{(card >= 0 ? $",\"card\":{card}" : "")}}}");
    }

    private static string Cells(char[] cells) => new(cells);

    private string Skins(int[] skins)
    {
        var sb = new StringBuilder(skins.Length);
        foreach (int s in skins) sb.Append(s < 0 ? '.' : (char)('0' + s));
        return sb.ToString();
    }

    private void Say(Link link, string line) => link.Apply(line);

    private void Start(Link link)
    {
        Say(link, "{\"t\":\"hello\",\"side\":\"rimworld\",\"protocol\":0,\"game_version\":\"demo\",\"window\":\"0\"}");
        Say(link, JsonSerializer.Serialize(new { t = "textures", dir = art.Dir.Replace('\\', '/'), names = art.Names }));
        Say(link, $"{{\"t\":\"terrain\",\"size\":[{Size},{Size}],\"cells\":{JsonSerializer.Serialize(Cells(terrain))}}}");
        Say(link, $"{{\"t\":\"roofs\",\"size\":[{Size},{Size}],\"cells\":{JsonSerializer.Serialize(Cells(roofs))},\"hills\":3}}");
        Say(link, $"{{\"t\":\"skins\",\"size\":[{Size},{Size}],\"walls\":{JsonSerializer.Serialize(Skins(wallSkin))},\"floors\":{JsonSerializer.Serialize(Skins(floorSkin))}}}");
        Say(link, $"{{\"t\":\"buildings\",\"buildings\":[{string.Join(",", buildings)}]}}");
        Items(link);
        Say(link, "{\"t\":\"plants\",\"plants\":[" + string.Join(",", plants.Select(p => $"{{\"x\":{p.x},\"z\":{p.z},\"kind\":\"{p.kind}\",\"growth\":{F(p.growth)}}}")) + "]}");
        Say(link, $"{{\"t\":\"plant_art\",\"kinds\":[{{\"kind\":\"oak\",\"tex\":[{art["TreeOak_cut"]}]}},{{\"kind\":\"pine\",\"tex\":[{art["TreePine_cut"]}]}},{{\"kind\":\"bush\",\"tex\":[{art["Bush_cut"]}]}},{{\"kind\":\"grass\",\"tex\":[{art["Grass_cut"]}]}},{{\"kind\":\"crop_Plant_Corn\",\"tex\":[{art["Corn_cut"]}]}}]}}");
        Say(link, $"{{\"t\":\"costs\",\"size\":[{Size},{Size}],\"cells\":{JsonSerializer.Serialize(Costs())}}}");
        Say(link, $"{{\"t\":\"glow\",\"size\":[{Size},{Size}],\"cells\":\"{Glow()}\"}}");
        Say(link, "{\"t\":\"looks\",\"looks\":[" + string.Join(",", pawns.Where(p => p.Kind == "humanlike").Select(LookJson)) + "]}");
        Weather(link);
        Doors(link);
    }

    private void Items(Link link) =>
        Say(link, "{\"t\":\"items\",\"items\":[" + string.Join(",", items.Select(i => $"{{\"def\":\"{i.def}\",\"x\":{i.x},\"z\":{i.z},\"tex\":{i.tex},\"count\":{i.count}}}")) + "]}");

    private void Weather(Link link)
    {
        float rain = launch.Rain, fog = launch.Fog;
        Say(link, $"{{\"t\":\"weather\",\"rain\":{F(rain)},\"snow\":0,\"fog\":{F(fog)},\"wind\":0.6,\"sky\":{F(sky)}}}");
    }

    private string Costs()
    {
        var sb = new StringBuilder(Size * Size);
        foreach (char c in terrain)
            sb.Append((char)('0' + c switch { '#' or 'W' or 'M' or 'O' => 1001, 'T' => 42, '~' => 30, 'c' => 14, 'g' => 2, _ => 0 }));
        return sb.ToString();
    }

    private string Glow()
    {
        var sb = new StringBuilder(Size * Size);
        var lamps = new[] { (38.5f, 33.5f), (46.5f, 32.5f), (57.5f, 35.5f) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                if (roofs[I(x, z)] == '.') { sb.Append('0'); continue; }
                float best = 0;
                foreach (var (lx, lz) in lamps)
                {
                    float d = MathF.Sqrt((x + 0.5f - lx) * (x + 0.5f - lx) + (z + 0.5f - lz) * (z + 0.5f - lz));
                    best = Math.Max(best, 1f - d / 7.5f);
                }
                sb.Append("0123456789abcdef"[Math.Clamp((int)MathF.Round(best * 2f * 15f), 0, 15)]);
            }
        return sb.ToString();
    }

    private string LookJson(Pawn p)
    {
        var (_, skin, hair, shirt, pants, _) = DemoArt.People.First(x => x.key == p.Look);
        string Rgb(Raylib_cs.Color c) => $"[{c.R},{c.G},{c.B}]";
        return $"{{\"id\":\"{p.Id}\",\"skin\":{Rgb(skin)},\"hair\":{Rgb(hair)},\"wear\":[{{\"def\":\"Apparel_Shirt\",\"color\":{Rgb(shirt)},\"tex\":-1,\"layer\":\"OnSkin\",\"parts\":[\"Torso\",\"Shoulders\",\"Arms\"]}},{{\"def\":\"Apparel_Pants\",\"color\":{Rgb(pants)},\"tex\":-1,\"layer\":\"OnSkin\",\"parts\":[\"Legs\"]}}]}}";
    }

    private void Doors(Link link) =>
        Say(link, "{\"t\":\"doors\",\"doors\":[" + string.Join(",", doors.Select(d =>
            $"{{\"at\":[{d.Key.Item1},{d.Key.Item2}],\"open\":{(int)MathF.Round(d.Value * 16)},\"axis\":{(Axis(d.Key) ? 1 : 0)}}}")) + "],\"steps\":16}");

    /// <summary>Whether a door's leaves slide north and south: it sits in a wall running north-south.</summary>
    private bool Axis((int x, int z) at) => Inside(at.x, at.z + 1) && terrain[I(at.x, at.z + 1)] == 'W';

    private bool Walkable(int x, int z)
    {
        if (!Inside(x, z)) return false;
        char c = terrain[I(x, z)];
        return c is not ('#' or 'W' or 'M' or 'O') && !(c == '+' && doors.TryGetValue((x, z), out float o) && o < 0.5f);
    }

    public void Update(float dt, Link link, Player player)
    {
        if (!started)
        {
            started = true;
            Start(link);
        }
        time += dt;
        Think(dt, link);
        sinceDoors += dt;
        if (sinceDoors > 0.05f)
        {
            sinceDoors = 0;
            OpenDoors(link);
        }
        sinceFires += dt;
        if (sinceFires > 0.25f)
        {
            sinceFires = 0;
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                var f = fires[i];
                f.left -= 0.25f;
                if (f.left <= 0) fires.RemoveAt(i);
                else fires[i] = f;
            }
            Say(link, "{\"t\":\"fires\",\"fires\":[" + string.Join(",", fires.Select(f => $"{{\"x\":{F(f.x)},\"z\":{F(f.z)},\"size\":{F(f.size)}}}")) + "]}");
            Say(link, "{\"t\":\"filth\",\"filth\":[" + string.Join(",", filth.Select(f => $"{{\"tex\":{art["FilthBlood_cut"]},\"size\":0.9,\"x\":{F(f.x)},\"z\":{F(f.z)},\"rot\":{F(f.rot)}}}")) + "]}");
            Loot(link);
            Status(link);
        }
        sinceSnapshot += dt;
        if (sinceSnapshot >= 0.1f)
        {
            sinceSnapshot = 0;
            Snapshot(link);
        }
    }

    /// <summary>The raid: walk at the player, shoot when in range, swing when close, throw now and then.</summary>
    private void Think(float dt, Link link)
    {
        Pawn you = Player;
        foreach (Pawn p in pawns)
        {
            if (p.Dead || p.Downed || p.Faction != "hostile") continue;
            float dx = you.X - p.X, dz = you.Z - p.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            float want = p.Melee ? 1.2f : 16f;
            p.Aim = -1;
            if (d > want)
            {
                float step = 2.6f * dt;
                float nx = p.X + dx / d * step, nz = p.Z + dz / d * step;
                if (Walkable((int)nx, (int)p.Z)) p.X = nx;
                if (Walkable((int)p.X, (int)nz)) p.Z = nz;
                p.Rot = Figures.Heading(dx, dz);
            }
            if (launch.Screenshot != null) continue;
            p.Cooldown -= dt;
            if (p.Melee)
            {
                if (d <= 1.5f && p.Cooldown <= 0)
                {
                    p.Cooldown = 1.6f;
                    p.Swings++;
                    p.Aim = Figures.Heading(dx, dz);
                    you.Health = Math.Max(0.1f, you.Health - 0.07f);
                    you.Hurt++;
                }
                continue;
            }
            if (d > 26f || !Sight(p.X, p.Z, you.X, you.Z)) continue;
            p.Aim = Figures.Heading(dx, dz);
            if (p.Grenades && p.Cooldown <= 0 && d < 12f && random.NextDouble() < 0.02)
            {
                p.Cooldown = 6f;
                var shell = new Shell { From = new[] { p.X, 1.3f, p.Z }, To = new[] { you.X + 1, 0f, you.Z }, Seconds = 0.9f, Tex = art["Proj_GrenadeFrag_cut"], Size = 1f, Lob = 1.7f, Fuse = 1.6f };
                Say(link, $"{{\"t\":\"shell\",\"shooter\":\"{p.Id}\",\"weapon\":\"Proj_GrenadeFrag\",\"from\":[{F(p.X)},1.3,{F(p.Z)}],\"to\":[{F(you.X + 1)},0,{F(you.Z)}],\"seconds\":0.9,\"arc\":false,\"tex\":{art["Proj_GrenadeFrag_cut"]},\"size\":1,\"lob\":1.7,\"fuse\":1.6}}");
                grenades.Add((time + 2.5f, shell, you.X + 1, you.Z));
                continue;
            }
            if (p.Cooldown > 0) continue;
            if (p.Burst <= 0)
            {
                p.Burst = p.Weapon == "Gun_Revolver" ? 1 : 3;
                p.NextBurst = 0;
            }
            p.NextBurst -= dt;
            if (p.NextBurst > 0) continue;
            p.NextBurst = 0.17f;
            p.Burst--;
            if (p.Burst <= 0) p.Cooldown = 1.8f + (float)random.NextDouble() * 1.5f;
            bool hit = random.NextDouble() < 0.45;
            float tx = you.X + (hit ? 0 : (float)(random.NextDouble() - 0.5) * 3f), tz = you.Z + (hit ? 0 : (float)(random.NextDouble() - 0.5) * 3f);
            string id = $"Bullet{++bullet}";
            Say(link, $"{{\"t\":\"shot\",\"shooter\":\"{p.Id}\",\"weapon\":\"{p.Weapon}\",\"from\":[{F(p.X)},1.3,{F(p.Z)}],\"to\":[{F(tx)},1.0,{F(tz)}],\"result\":\"{(hit ? "hit" : "miss")}\",\"speed\":45,\"id\":\"{id}\"{(hit ? $",\"target\":\"{you.Id}\"" : "")}}}");
        }
        for (int i = grenades.Count - 1; i >= 0; i--)
        {
            var (at, _, gx, gz) = grenades[i];
            if (time < at) continue;
            grenades.RemoveAt(i);
            Say(link, $"{{\"t\":\"explosion\",\"x\":{F(gx)},\"z\":{F(gz)},\"radius\":1.9,\"damage\":\"Bomb\"}}");
            fires.Add((gx + 0.3f, gz - 0.2f, 0.7f, 6f));
            float d = MathF.Sqrt((you.X - gx) * (you.X - gx) + (you.Z - gz) * (you.Z - gz));
            if (d < 2.5f)
            {
                you.Health = Math.Max(0.1f, you.Health - 0.2f);
                you.Hurt++;
            }
        }
    }

    private bool Sight(float x0, float z0, float x1, float z1)
    {
        float d = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        for (float t = 1; t < d; t += 0.5f)
        {
            int x = (int)(x0 + (x1 - x0) * t / d), z = (int)(z0 + (z1 - z0) * t / d);
            if (!Inside(x, z)) return false;
            char c = terrain[I(x, z)];
            if (c is '#' or 'W' or 'M' || c == '+' && doors[(x, z)] < 0.5f) return false;
        }
        return true;
    }

    /// <summary>As the mod's DoorReport: a door beside the played colonist opens for them, and
    /// shuts again a while after they have gone.</summary>
    private void OpenDoors(Link link)
    {
        bool moved = false;
        foreach (var at in doors.Keys.ToList())
        {
            bool near = pawns.Any(p => !p.Dead && !p.Downed && MathF.Abs(p.X - (at.Item1 + 0.5f)) < 1.6f && MathF.Abs(p.Z - (at.Item2 + 0.5f)) < 1.6f);
            if (near) doorHeld[at] = 1.5f;
            else doorHeld[at] = (doorHeld.TryGetValue(at, out float h) ? h : 0) - 0.05f;
            float open = doors[at];
            float want = doorHeld[at] > 0 ? 1f : 0f;
            float next = want > open ? Math.Min(1f, open + 0.12f) : Math.Max(0f, open - 0.12f);
            if (MathF.Abs(next - open) > 1e-4f)
            {
                doors[at] = next;
                moved = true;
            }
        }
        if (moved) Doors(link);
    }

    private void Snapshot(Link link)
    {
        var sb = new StringBuilder();
        sb.Append($"{{\"t\":\"snapshot\",\"tick\":{(long)(time * 60)},\"map\":[{Size},{Size}],\"pawns\":[");
        bool first = true;
        foreach (Pawn p in pawns)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"id\":\"{p.Id}\",\"name\":\"{p.Name}\",\"kind\":\"{p.Kind}\",\"x\":{F(p.X)},\"z\":{F(p.Z)},\"rot\":{F(Rot4(p.Rot))},\"stance\":\"{(p.Downed ? "prone" : "stand")}\"");
            if (p.Weapon != null) sb.Append($",\"weapon\":\"{p.Weapon}\"");
            if (p.Kind == "animal")
                sb.Append($",\"art\":[{art["Muffalo_north_cut"]},{art["Muffalo_east_cut"]},{art["Muffalo_south_cut"]}],\"draw\":[2.6,2.6]");
            else
                sb.Append($",\"art\":[{art[$"pawn_{p.Look}_0_cut"]},{art[$"pawn_{p.Look}_1_cut"]},{art[$"pawn_{p.Look}_2_cut"]}],\"draw\":[2,2]");
            if (p.Hurt > 0) sb.Append($",\"hurt\":{p.Hurt}");
            if (p.Downed) sb.Append($",\"lie\":{(p.Dead ? 90 : 270)}");
            if (p.Dead) sb.Append(",\"dead\":true");
            if (p.Swings > 0) sb.Append($",\"swings\":{p.Swings}");
            string? held = Held(p);
            if (held != null) sb.Append($",\"held\":{held}");
            sb.Append($",\"downed\":{(p.Downed ? "true" : "false")},\"faction\":\"{p.Faction}\"}}");
        }
        sb.Append("]");
        Pawn you = Player;
        bool rifle = you.Weapon == "Gun_AssaultRifle", revolver = you.Weapon == "Gun_Revolver", grenade = you.Weapon == "Weapon_GrenadeFrag";
        sb.Append($",\"player_pawn\":\"{you.Id}\",\"player_speed\":4.6,\"rate\":1");
        if (rifle) sb.Append(",\"player_gun\":{\"burst\":3,\"between\":0.17,\"warmup\":0.6,\"cooldown\":1.2,\"range\":30.9}");
        if (revolver) sb.Append(",\"player_gun\":{\"burst\":1,\"between\":0,\"warmup\":0.3,\"cooldown\":1.0,\"range\":25.9}");
        if (grenade) sb.Append(",\"player_gun\":{\"burst\":1,\"between\":0,\"warmup\":1.0,\"cooldown\":2.0,\"throw\":12.9}");
        sb.Append($",\"player_health\":{F(you.Health)},\"player_armor\":0.45");
        if (you.Weapon != null) sb.Append($",\"player_weapon\":\"{(rifle ? "assault rifle" : revolver ? "revolver" : grenade ? "frag grenades" : you.Weapon.Contains("LongSword") ? "longsword" : "knife")}\"");
        sb.Append('}');
        Say(link, sb.ToString());
        you.Health = Math.Min(1f, you.Health + 0.0015f);
    }

    /// <summary>As the mod's StatusReport: the played colonist's limbs, pain and wounds.</summary>
    private void Status(Link link)
    {
        Pawn you = Player;
        float hurt = 1f - you.Health;
        string Part(string name, float health, bool missing = false) =>
            $"{{\"part\":\"{name}\",\"health\":{F(Math.Clamp(health, 0, 1))}{(missing ? ",\"missing\":true" : "")}}}";
        var parts = new[]
        {
            Part("head", 1f - hurt * 0.3f), Part("torso", 1f - hurt * 1.2f), Part("left_arm", 1f - hurt * 0.4f),
            Part("right_arm", 0.9f - hurt * 0.2f), Part("left_leg", 1f), Part("right_leg", 1f - hurt * 0.8f),
        };
        string conditions = hurt > 0.05f
            ? "[{\"label\":\"Gunshot\",\"part\":\"torso\",\"bleeding\":true},{\"label\":\"Cut\",\"part\":\"right leg\"},{\"label\":\"Scratch\",\"part\":\"right arm\"}]"
            : "[{\"label\":\"Scratch\",\"part\":\"right arm\"}]";
        Say(link, $"{{\"t\":\"status\",\"pawn\":\"{you.Id}\",\"health\":{F(you.Health)},\"pain\":{F(hurt * 0.6f + 0.02f)},\"bleeding\":{F(hurt > 0.05f ? hurt * 1.5f : 0)}"
            + (hurt > 0.05f ? $",\"bleed_hours\":{F(14f - hurt * 20f)}" : "")
            + $",\"mood\":0.62,\"capacities\":{{\"consciousness\":{F(1f - hurt * 0.2f)},\"moving\":{F(1f - hurt * 0.6f)},\"manipulation\":0.95,\"sight\":1}}"
            + $",\"parts\":[{string.Join(",", parts)}],\"conditions\":{conditions}}}");
    }

    private static float Rot4(float heading) => ((int)MathF.Round(heading / 90f) % 4 + 4) % 4 * 90;

    private string? Held(Pawn p)
    {
        if (p.Weapon == null || p.Downed || p.Kind != "humanlike") return null;
        string key = p.Weapon switch
        {
            "Gun_AssaultRifle" => "Gun_AssaultRifle_cut",
            "Gun_Revolver" => "Gun_Revolver_cut",
            "MeleeWeapon_Knife" => "MeleeWeapon_Knife_cut",
            "MeleeWeapon_LongSword" => "MeleeWeapon_LongSword_cut",
            _ => "Weapon_GrenadeFrag_cut",
        };
        float size = p.Weapon switch { "Gun_AssaultRifle" => 1.4f, "MeleeWeapon_LongSword" => 1.4f, "Gun_Revolver" => 0.9f, _ => 0.8f };
        var sb = new StringBuilder($"{{\"tex\":{art[key]},\"size\":[{F(size)},{F(size)}]");
        if (p.Aim >= 0) sb.Append($",\"aim\":{F(p.Aim)}");
        if (p.Weapon.StartsWith("Melee")) sb.Append(",\"turn\":-65,\"melee\":true");
        return sb.Append('}').ToString();
    }

    private void Loot(Link link)
    {
        Pawn you = Player;
        string Item(string id, string label, string def, string kind, float mass, int count = 1) =>
            $"{{\"id\":\"{id}\",\"label\":\"{label}\",\"def\":\"{def}\",\"count\":{count},\"kind\":\"{kind}\",\"mass\":{F(mass)}}}";
        var kit = new List<string>();
        if (you.Weapon != null) kit.Add(Item("Weapon1", Hud_Pretty(you.Weapon), you.Weapon, "weapon", 3.5f));
        kit.Add(Item("Apparel1", "flak vest (normal)", "Apparel_FlakVest", "apparel", 0.8f));
        kit.Add(Item("Medicine1", "industrial medicine", "MedicineIndustrial", "item", 0.5f, 2));
        var nearby = new List<string>();
        var ground = items.Where(i => MathF.Abs(i.x + 0.5f - you.X) <= 1.6f && MathF.Abs(i.z + 0.5f - you.Z) <= 1.6f)
            .Select(i => Item($"{i.def}{i.x}_{i.z}", $"{Hud_Pretty(i.def)} x{i.count}", i.def, "item", 0.5f * i.count, i.count)).ToList();
        nearby.Add($"{{\"id\":\"ground\",\"label\":\"Ground\",\"items\":[{string.Join(",", ground)}]}}");
        foreach (Pawn body in pawns.Where(p => p.Downed && p != you && MathF.Abs(p.X - you.X) <= 1.6f && MathF.Abs(p.Z - you.Z) <= 1.6f))
            nearby.Add($"{{\"id\":\"{body.Id}\",\"label\":\"{body.Name}{(body.Dead ? " (dead)" : "")}\",\"items\":[{(body.Weapon != null ? Item(body.Id + "w", Hud_Pretty(body.Weapon), body.Weapon, "weapon", 2f) : "")}]}}");
        Say(link, $"{{\"t\":\"loot\",\"pawn\":\"{you.Id}\",\"mass\":12.4,\"capacity\":35,\"kit\":[{string.Join(",", kit)}],\"nearby\":[{string.Join(",", nearby)}]}}");
    }

    private static string Hud_Pretty(string def) => UI.Hud.Pretty(def) ?? def;

    /// <summary>What the client sends, as RimWorld would take it.</summary>
    public void Receive(string line, Link link)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement e = doc.RootElement;
        Pawn you = Player;
        switch (e.Str("t"))
        {
            case "player_move":
                {
                    float x = e.Num("x"), z = e.Num("z");
                    if (Walkable((int)MathF.Floor(x), (int)MathF.Floor(z)))
                    {
                        you.X = x;
                        you.Z = z;
                    }
                    you.Rot = e.Num("yaw");
                    break;
                }
            case "player_shot":
                {
                    if (e.Str("target") is not string target) break;
                    Pawn? victim = pawns.FirstOrDefault(p => p.Id == target);
                    if (victim == null || victim.Dead) break;
                    victim.Hurt++;
                    victim.Health -= e.Str("part") == "head" ? 0.5f : 0.22f;
                    Bleed(victim);
                    if (victim.Health < 0.35f) victim.Downed = true;
                    if (victim.Health <= 0)
                    {
                        victim.Dead = true;
                        victim.Downed = true;
                    }
                    break;
                }
            case "player_melee":
                {
                    Pawn? victim = pawns.FirstOrDefault(p => p.Id == e.Str("target"));
                    if (victim == null || victim.Dead) break;
                    you.Swings++;
                    victim.Hurt++;
                    victim.Health -= 0.3f;
                    Bleed(victim);
                    if (victim.Health < 0.35f) victim.Downed = true;
                    break;
                }
            case "player_struck":
                you.Health = Math.Max(0.1f, you.Health - (e.Str("part") == "head" ? 0.15f : 0.06f));
                you.Hurt++;
                break;
            case "player_throw":
                {
                    float[] to = e.Floats("to") ?? new[] { you.X, you.Z };
                    Say(link, $"{{\"t\":\"shell\",\"shooter\":\"{you.Id}\",\"weapon\":\"Proj_GrenadeFrag\",\"from\":[{F(you.X)},1.3,{F(you.Z)}],\"to\":[{F(to[0])},0,{F(to[1])}],\"seconds\":0.9,\"arc\":false,\"tex\":{art["Proj_GrenadeFrag_cut"]},\"size\":1,\"lob\":1.7,\"fuse\":1.6}}");
                    grenades.Add((time + 2.5f, new Shell(), to[0], to[1]));
                    break;
                }
            case "use":
                foreach (var at in doors.Keys.ToList())
                    if (MathF.Abs(at.Item1 + 0.5f - you.X) < 1.8f && MathF.Abs(at.Item2 + 0.5f - you.Z) < 1.8f)
                        doorHeld[at] = doors[at] > 0.5f ? -1f : 4f;
                break;
            case "loot_take":
                {
                    string item = e.Str("item") ?? "";
                    if (item.EndsWith("w"))
                    {
                        Pawn? from = pawns.FirstOrDefault(p => p.Id + "w" == item);
                        if (from != null)
                            (you.Weapon, from.Weapon) = (from.Weapon, you.Weapon);
                    }
                    else items.RemoveAll(i => $"{i.def}{i.x}_{i.z}" == item);
                    Items(link);
                    break;
                }
            case "loot_drop":
                if (e.Str("item") == "Weapon1" && you.Weapon != null)
                {
                    items.Add((you.Weapon, (int)you.X, (int)you.Z, art[Held(you) is string ? WeaponArt(you.Weapon) : "Steel_cut"], 1));
                    you.Weapon = null;
                    Items(link);
                }
                break;
        }
    }

    private static string WeaponArt(string def) => def switch
    {
        "Gun_AssaultRifle" => "Gun_AssaultRifle_cut",
        "Gun_Revolver" => "Gun_Revolver_cut",
        "MeleeWeapon_LongSword" => "MeleeWeapon_LongSword_cut",
        _ => "MeleeWeapon_Knife_cut",
    };

    private void Bleed(Pawn victim) => filth.Add((victim.X + (float)(random.NextDouble() - 0.5), victim.Z + (float)(random.NextDouble() - 0.5), (float)random.NextDouble() * 360));
}
