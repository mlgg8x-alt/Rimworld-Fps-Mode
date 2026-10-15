using System.Text.Json;

namespace RimFps.Net;

/// <summary>
/// Everything RimWorld has said, as it stands now: the latest of each kind of state, with a
/// revision number that goes up whenever it changes, and the one-off events (shots, shells,
/// explosions, flecks) queued for the game to take each frame. Main thread only.
/// </summary>
public sealed class Link
{
    public bool Connected;
    /// <summary>RimWorld's own window, from its <c>hello</c>, for Tab to bring up.</summary>
    public long RimworldWindow;
    public string? GameVersion;

    public int Snapshots;
    public Snapshot? Snap;
    public int MapW, MapH;

    public string TexturesDir = "";
    public List<string> TextureNames = new();
    public int TexturesRev;

    public string? Terrain;
    public int TerrainRev;
    public string? Roofs;
    public int Hills;
    public int RoofsRev;
    public string? WallSkins, FloorSkins;
    public int SkinsRev;
    public List<Building> Buildings = new();
    public int BuildingsRev;
    public List<Item> Items = new();
    public int ItemsRev;
    public List<Plant> Plants = new();
    public int PlantsRev;
    public Dictionary<string, List<int>> PlantArt = new();
    public int PlantArtRev;
    public string? Costs;
    public int CostsRev;
    public string? Glow;
    public int GlowRev;
    public List<DoorState> Doors = new();
    public int DoorSteps = 16;
    public int DoorsRev;
    public PawnStatus? Status;
    public int StatusRev;
    public Loot? Loot;
    public int LootRev;
    public Dictionary<string, Look> Looks = new();
    public List<Fire> Fires = new();
    public List<Gas> Gas = new();
    public Weather Weather = new();
    public List<Filth> Filth = new();
    public int FilthRev;
    /// <summary>RimWorld's own window sits over our top left corner as the minimap.</summary>
    public bool Overlay;

    public readonly List<Shot> Shots = new();
    public readonly List<Shell> Shells = new();
    public readonly List<Explosion> Explosions = new();
    public readonly List<FleckState> Flecks = new();

    /// <summary>Messages of each kind seen, for the debug overlay.</summary>
    public readonly Dictionary<string, int> Seen = new();
    public int Errors;

    /// <summary>The pawn the player plays, from the latest snapshot.</summary>
    public PawnState? Player =>
        Snap?.PlayerPawn is string id ? Snap.Pawns.FirstOrDefault(p => p.Id == id) : null;

    public bool Paused => Snap?.Paused == true;
    public float Rate => Snap?.Rate is float r && !Paused ? Math.Clamp(r, 0.1f, 6f) : 1f;

    /// <summary>A new connection: forget the old game.</summary>
    public void Reset()
    {
        var keep = Seen;
        int errors = Errors;
        var fresh = new Link();
        foreach (var field in typeof(Link).GetFields())
            if (!field.IsInitOnly) field.SetValue(this, field.GetValue(fresh));
        Shots.Clear();
        Shells.Clear();
        Explosions.Clear();
        Flecks.Clear();
        Errors = errors;
        _ = keep;
    }

    public void Apply(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            string type = e.Str("t") ?? "";
            Seen[type] = Seen.TryGetValue(type, out int n) ? n + 1 : 1;
            Apply(type, e);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            Errors++;
            if (Errors < 20) Console.WriteLine($"[fps] could not read a line from RimWorld: {ex.Message}");
        }
    }

    private void Apply(string type, JsonElement e)
    {
        switch (type)
        {
            case "hello":
                if (long.TryParse(e.Str("window"), out long window)) RimworldWindow = window;
                GameVersion = e.Str("game_version");
                Console.WriteLine($"[fps] RimWorld says hello ({GameVersion ?? "unknown version"})");
                break;
            case "tick":
                break;
            case "snapshot":
                Snap = Read.Snapshot(e);
                Snapshots++;
                if (Snap.Map is { Length: 2 } map)
                {
                    MapW = map[0];
                    MapH = map[1];
                }
                break;
            case "textures":
                TexturesDir = e.Str("dir") ?? TexturesDir;
                var names = new List<string>();
                foreach (var n in e.Array("names"))
                    if (n.ValueKind == JsonValueKind.String) names.Add(n.GetString()!);
                TextureNames = names;
                TexturesRev++;
                break;
            case "terrain":
                Size(e);
                Terrain = e.Str("cells");
                TerrainRev++;
                break;
            case "roofs":
                Size(e);
                Roofs = e.Str("cells");
                Hills = e.Int("hills");
                RoofsRev++;
                break;
            case "skins":
                Size(e);
                WallSkins = e.Str("walls");
                FloorSkins = e.Str("floors");
                SkinsRev++;
                break;
            case "buildings":
                Buildings = e.Array("buildings").Select(Read.Building).ToList();
                BuildingsRev++;
                break;
            case "items":
                Items = e.Array("items").Select(Read.Item).ToList();
                ItemsRev++;
                break;
            case "plants":
                Plants = e.Array("plants").Select(Read.Plant).ToList();
                PlantsRev++;
                break;
            case "plant_art":
                var art = new Dictionary<string, List<int>>();
                foreach (var k in e.Array("kinds"))
                {
                    string? kind = k.Str("kind");
                    if (kind != null) art[kind] = (k.Ints("tex") ?? System.Array.Empty<int>()).ToList();
                }
                PlantArt = art;
                PlantArtRev++;
                break;
            case "costs":
                Size(e);
                Costs = e.Str("cells");
                CostsRev++;
                break;
            case "glow":
                Size(e);
                Glow = e.Str("cells");
                GlowRev++;
                break;
            case "doors":
                Doors = e.Array("doors").Select(Read.Door).ToList();
                DoorSteps = Math.Max(1, e.Int("steps", 16));
                DoorsRev++;
                break;
            case "status":
                Status = PawnStatus.Read(e);
                StatusRev++;
                break;
            case "loot":
                Loot = Read.Loot(e);
                LootRev++;
                break;
            case "looks":
                var looks = new Dictionary<string, Look>();
                foreach (var l in e.Array("looks"))
                {
                    var look = Read.Look(l);
                    looks[look.Id] = look;
                }
                Looks = looks;
                break;
            case "shot":
                if (Shots.Count < 512) Shots.Add(Read.Shot(e));
                break;
            case "shell":
                if (Shells.Count < 256) Shells.Add(Read.Shell(e));
                break;
            case "explosion":
                if (Explosions.Count < 128)
                    Explosions.Add(new Explosion { X = e.Num("x"), Z = e.Num("z"), Radius = e.Num("radius", 1), Damage = e.Str("damage") ?? "Bomb" });
                break;
            case "fires":
                Fires = e.Array("fires").Select(f => new Fire { X = f.Num("x"), Z = f.Num("z"), Size = f.Num("size", 0.5f) }).ToList();
                break;
            case "gas":
                Gas = e.Array("gas").Select(g =>
                {
                    int[] at = g.Ints("at") is { Length: 2 } a ? a : new int[2];
                    return new Gas { X = at[0], Z = at[1], Kind = g.Str("kind") ?? "smoke", Density = g.Int("density", 100) };
                }).ToList();
                break;
            case "weather":
                Weather = new Weather
                {
                    Rain = e.Num("rain"),
                    Snow = e.Num("snow"),
                    Fog = e.Num("fog"),
                    Wind = e.Num("wind"),
                    Sky = e.NumOpt("sky") ?? 1f,
                };
                break;
            case "filth":
                Filth = e.Array("filth").Select(Read.Filth).ToList();
                FilthRev++;
                break;
            case "flecks":
                foreach (var f in e.Array("flecks"))
                    if (Flecks.Count < 400) Flecks.Add(Read.Fleck(f));
                break;
            case "overlay":
                Overlay = e.Bool("shown");
                break;
            case "view":
                // Upstream's streamed minimap picture: RimWorld's own window is the minimap now.
                break;
            default:
                if (Seen[type] == 1) Console.WriteLine($"[fps] RimWorld sent '{type}', which this client does not know: skipped");
                break;
        }
    }

    private void Size(JsonElement e)
    {
        if (e.Ints("size") is { Length: 2 } size && size[0] > 0 && size[1] > 0)
        {
            MapW = size[0];
            MapH = size[1];
        }
    }
}
