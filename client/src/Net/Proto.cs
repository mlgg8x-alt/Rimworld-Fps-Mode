using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RimFps.Net;

// The messages of protocol/PROTOCOL.md, as the client reads them. Field names follow the
// protocol; units are RimWorld's (cells, degrees clockwise from north, game seconds).

public sealed class PawnState
{
    public string Id = "";
    public string? Name;
    public string Kind = "other";
    public float X, Z, Rot;
    public string Stance = "stand";
    public string? Weapon;
    public bool Downed, Dead, Bed;
    public string Faction = "neutral";
    /// <summary>Pictures facing north, east and south (west is east mirrored), and how many
    /// cells across and up RimWorld draws them.</summary>
    public int[]? Art;
    public float[]? Draw;
    public int Hurt, Swings;
    public float? Lie;
    public Carried? Carry;
    public Held? Held;

    public bool Humanlike => Kind == "humanlike";
    public bool Hostile => Faction == "hostile";
}

public sealed class Held
{
    public int Tex;
    public float W = 1, H = 1;
    public float? Aim;
    public float Turn, Reach;
    public bool Melee;
}

public sealed class Carried
{
    public int Tex;
    public float Size = 1;
    public bool Pawn;
}

public sealed class GunTiming
{
    public int Burst = 1;
    public float Between, Warmup, Cooldown;
    public float? Throw, Range;
}

public sealed class Snapshot
{
    public long Tick;
    public int[]? Map;
    public List<PawnState> Pawns = new();
    public string? PlayerPawn;
    public float? PlayerSpeed, Rate, Health, Armor;
    public GunTiming? Gun;
    public bool Paused;
    public string? PlayerWeapon;
}

public sealed class Shot
{
    public string Shooter = "", Weapon = "";
    public float[] From = new float[3], To = new float[3];
    public bool Hit;
    public float? Speed;
    public string? Target, Id;
}

public sealed class Shell
{
    public string Shooter = "", Weapon = "";
    public float[] From = new float[3], To = new float[3];
    public float Seconds = 1;
    public bool Arc;
    public int? Tex;
    public float? Size, Lob, Fuse;
}

public sealed class Explosion
{
    public float X, Z, Radius = 1;
    public string Damage = "Bomb";
}

public sealed class LootItem
{
    public string Id = "", Label = "", Def = "", Kind = "item";
    public int Count = 1;
    public float Mass;
}

public sealed class LootSource
{
    public string Id = "", Label = "";
    public List<LootItem> Items = new();
}

public sealed class Loot
{
    public string Pawn = "";
    public float Mass, Capacity;
    public List<LootItem> Kit = new();
    public List<LootSource> Nearby = new();
}

public sealed class Building
{
    public string Def = "";
    public int[] Rect = new int[4];
    public int Rot;
    public float[] At = new float[2], Draw = new float[] { 1, 1 };
    public int Tex = -1, Turn, Card = -1;
    public bool Flip, Solid;
    public float Fill;
}

public sealed class Item
{
    public string Def = "";
    public int X, Z, Tex = -1, Count = 1;
}

public sealed class Plant
{
    public int X, Z;
    public string Kind = "";
    public float Growth = 1;
}

public sealed class DoorState
{
    public int X, Z, Open, Axis;
    public int[]? Rect;
}

public sealed class Fire
{
    public float X, Z, Size;
}

public sealed class Gas
{
    public int X, Z, Density;
    public string Kind = "smoke";
}

public sealed class Weather
{
    public float Rain, Snow, Fog, Wind, Sky = 1;
}

public sealed class Filth
{
    public int Tex;
    public float Size = 1, X, Z, Rot;
}

public sealed class FleckState
{
    public int Tex;
    public float X, Z, Y, Size = 1, Grow, Rot, Spin, Vx, Vz, Vy, FadeIn, Solid, FadeOut;
    public float[]? Color;
    public bool Flat;
}

public sealed class Wear
{
    public string Def = "", Layer = "";
    public byte[] Color = { 200, 200, 200 };
    public int Tex = -1;
    public List<string> Parts = new();
}

public sealed class Look
{
    public string Id = "";
    public byte[] Skin = { 220, 180, 150 };
    public byte[]? Hair;
    public List<Wear> Wear = new();
}

/// <summary>FPS fork: how the played colonist is, from RimWorld's health tab.</summary>
public sealed class PawnStatus
{
    public string Pawn = "";
    public float Health = 1, Pain, Bleeding;
    public float? BleedHours, Mood;
    public readonly Dictionary<string, float> Capacities = new();
    /// <summary>Each limb (head, torso, left_arm, right_arm, left_leg, right_leg): health 0 to 1, missing.</summary>
    public readonly Dictionary<string, (float health, bool missing)> Parts = new();
    public readonly List<(string label, string? part, bool bleeding)> Conditions = new();

    public static PawnStatus Read(JsonElement e)
    {
        var s = new PawnStatus
        {
            Pawn = e.Str("pawn") ?? "",
            Health = e.Num("health", 1),
            Pain = e.Num("pain"),
            Bleeding = e.Num("bleeding"),
            BleedHours = e.NumOpt("bleed_hours"),
            Mood = e.NumOpt("mood"),
        };
        if (e.TryGetProperty("capacities", out var caps) && caps.ValueKind == JsonValueKind.Object)
            foreach (var c in caps.EnumerateObject())
                if (c.Value.ValueKind == JsonValueKind.Number) s.Capacities[c.Name] = c.Value.GetSingle();
        foreach (var p in e.Array("parts"))
            if (p.Str("part") is string part) s.Parts[part] = (p.Num("health", 1), p.Bool("missing"));
        foreach (var c in e.Array("conditions"))
            if (c.Str("label") is string label) s.Conditions.Add((label, c.Str("part"), c.Bool("bleeding")));
        return s;
    }
}

/// <summary>Reading JSON values loosely: a missing or odd value is the default, never an error,
/// so one side can add fields without breaking the other.</summary>
public static class J
{
    public static string? Str(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static float Num(this JsonElement e, string key, float fallback = 0) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    public static float? NumOpt(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : null;

    public static int Int(this JsonElement e, string key, int fallback = 0) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : fallback;

    public static long Long(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : 0;

    public static bool Bool(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && (v.ValueKind == JsonValueKind.True
            || v.ValueKind == JsonValueKind.String && v.GetString() == "true");

    public static float[]? Floats(this JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new float[v.GetArrayLength()];
        int i = 0;
        foreach (var x in v.EnumerateArray()) list[i++] = x.ValueKind == JsonValueKind.Number ? x.GetSingle() : 0;
        return list;
    }

    public static int[]? Ints(this JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new int[v.GetArrayLength()];
        int i = 0;
        foreach (var x in v.EnumerateArray()) list[i++] = x.ValueKind == JsonValueKind.Number ? (int)Math.Round(x.GetDouble()) : 0;
        return list;
    }

    public static IEnumerable<JsonElement> Array(this JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (var x in v.EnumerateArray()) yield return x;
    }

    public static bool Has(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null;
}

/// <summary>Parses each message's body into the types above.</summary>
public static class Read
{
    public static PawnState Pawn(JsonElement e)
    {
        var p = new PawnState
        {
            Id = e.Str("id") ?? "",
            Name = e.Str("name"),
            Kind = e.Str("kind") ?? "other",
            X = e.Num("x"),
            Z = e.Num("z"),
            Rot = e.Num("rot"),
            Stance = e.Str("stance") ?? "stand",
            Weapon = e.Str("weapon"),
            Downed = e.Bool("downed"),
            Dead = e.Bool("dead"),
            Bed = e.Bool("bed"),
            Faction = e.Str("faction") ?? "neutral",
            Art = e.Ints("art"),
            Draw = e.Floats("draw"),
            Hurt = e.Int("hurt"),
            Swings = e.Int("swings"),
            Lie = e.NumOpt("lie"),
        };
        if (p.Art is { Length: < 3 }) p.Art = null;
        if (p.Draw is { Length: < 2 }) p.Draw = null;
        if (e.TryGetProperty("carry", out var c) && c.ValueKind == JsonValueKind.Object)
            p.Carry = new Carried { Tex = c.Int("tex", -1), Size = c.Num("size", 1), Pawn = c.Bool("pawn") };
        if (e.TryGetProperty("held", out var h) && h.ValueKind == JsonValueKind.Object)
        {
            float[]? size = h.Floats("size");
            p.Held = new Held
            {
                Tex = h.Int("tex", -1),
                W = size is { Length: >= 2 } ? size[0] : 1,
                H = size is { Length: >= 2 } ? size[1] : 1,
                Aim = h.NumOpt("aim"),
                Turn = h.Num("turn"),
                Reach = h.Num("reach"),
                Melee = h.Bool("melee"),
            };
        }
        return p;
    }

    public static Snapshot Snapshot(JsonElement e)
    {
        var s = new Snapshot
        {
            Tick = e.Long("tick"),
            Map = e.Ints("map"),
            PlayerPawn = e.Str("player_pawn"),
            PlayerSpeed = e.NumOpt("player_speed"),
            Rate = e.NumOpt("rate"),
            Paused = e.Bool("paused"),
            Health = e.NumOpt("player_health"),
            Armor = e.NumOpt("player_armor"),
            PlayerWeapon = e.Str("player_weapon"),
        };
        foreach (var p in e.Array("pawns")) s.Pawns.Add(Pawn(p));
        if (e.TryGetProperty("player_gun", out var g) && g.ValueKind == JsonValueKind.Object)
            s.Gun = new GunTiming
            {
                Burst = Math.Max(1, g.Int("burst", 1)),
                Between = g.Num("between"),
                Warmup = g.Num("warmup"),
                Cooldown = g.Num("cooldown"),
                Throw = g.NumOpt("throw"),
                Range = g.NumOpt("range"),
            };
        return s;
    }

    public static Shot Shot(JsonElement e) => new()
    {
        Shooter = e.Str("shooter") ?? "",
        Weapon = e.Str("weapon") ?? "",
        From = Point(e.Floats("from")),
        To = Point(e.Floats("to")),
        Hit = e.Str("result") == "hit",
        Speed = e.NumOpt("speed"),
        Target = e.Str("target"),
        Id = e.Str("id"),
    };

    public static Shell Shell(JsonElement e) => new()
    {
        Shooter = e.Str("shooter") ?? "",
        Weapon = e.Str("weapon") ?? "",
        From = Point(e.Floats("from")),
        To = Point(e.Floats("to")),
        Seconds = e.Num("seconds", 1),
        Arc = e.Bool("arc"),
        Tex = e.Has("tex") ? e.Int("tex", -1) : null,
        Size = e.NumOpt("size"),
        Lob = e.NumOpt("lob"),
        Fuse = e.NumOpt("fuse"),
    };

    private static float[] Point(float[]? p) =>
        p is { Length: >= 3 } ? p : p is { Length: 2 } ? new[] { p[0], 0f, p[1] } : new float[3];

    public static Loot Loot(JsonElement e)
    {
        var loot = new Loot { Pawn = e.Str("pawn") ?? "", Mass = e.Num("mass"), Capacity = e.Num("capacity") };
        foreach (var i in e.Array("kit")) loot.Kit.Add(LootItem(i));
        foreach (var s in e.Array("nearby"))
        {
            var source = new LootSource { Id = s.Str("id") ?? "", Label = s.Str("label") ?? "" };
            foreach (var i in s.Array("items")) source.Items.Add(LootItem(i));
            loot.Nearby.Add(source);
        }
        return loot;
    }

    private static LootItem LootItem(JsonElement e) => new()
    {
        Id = e.Str("id") ?? "",
        Label = e.Str("label") ?? "",
        Def = e.Str("def") ?? "",
        Kind = e.Str("kind") ?? "item",
        Count = e.Int("count", 1),
        Mass = e.Num("mass"),
    };

    public static Building Building(JsonElement e) => new()
    {
        Def = e.Str("def") ?? "",
        Rect = e.Ints("rect") is { Length: 4 } r ? r : new int[4],
        Rot = e.Int("rot"),
        At = e.Floats("at") is { Length: 2 } a ? a : new float[2],
        Draw = e.Floats("draw") is { Length: 2 } d ? d : new float[] { 1, 1 },
        Tex = e.Int("tex", -1),
        Turn = e.Int("turn"),
        Flip = e.Bool("flip"),
        Fill = e.Num("fill"),
        Solid = e.Bool("solid"),
        Card = e.Int("card", -1),
    };

    public static Item Item(JsonElement e) => new()
    {
        Def = e.Str("def") ?? "",
        X = e.Int("x"),
        Z = e.Int("z"),
        Tex = e.Int("tex", -1),
        Count = e.Int("count", 1),
    };

    public static Plant Plant(JsonElement e) => new()
    {
        X = e.Int("x"),
        Z = e.Int("z"),
        Kind = e.Str("kind") ?? "",
        Growth = e.Num("growth", 1),
    };

    public static DoorState Door(JsonElement e)
    {
        int[] at = e.Ints("at") is { Length: 2 } a ? a : new int[2];
        return new DoorState { X = at[0], Z = at[1], Open = e.Int("open"), Axis = e.Int("axis"), Rect = e.Ints("rect") };
    }

    public static Filth Filth(JsonElement e) => new()
    {
        Tex = e.Int("tex", -1),
        Size = e.Num("size", 1),
        X = e.Num("x"),
        Z = e.Num("z"),
        Rot = e.Num("rot"),
    };

    public static FleckState Fleck(JsonElement e) => new()
    {
        Tex = e.Int("tex", -1),
        X = e.Num("x"),
        Z = e.Num("z"),
        Y = e.Num("y"),
        Size = e.Num("size", 1),
        Grow = e.Num("grow"),
        Rot = e.Num("rot"),
        Spin = e.Num("spin"),
        Vx = e.Num("vx"),
        Vz = e.Num("vz"),
        Vy = e.Num("vy"),
        FadeIn = e.Num("fade_in"),
        Solid = e.Num("solid"),
        FadeOut = e.Num("fade_out"),
        Color = e.Floats("color"),
        Flat = e.Bool("flat"),
    };

    public static Look Look(JsonElement e)
    {
        var look = new Look { Id = e.Str("id") ?? "", Skin = Rgb(e, "skin") ?? new byte[] { 220, 180, 150 }, Hair = Rgb(e, "hair") };
        foreach (var w in e.Array("wear"))
        {
            var wear = new Wear
            {
                Def = w.Str("def") ?? "",
                Layer = w.Str("layer") ?? "",
                Color = Rgb(w, "color") ?? new byte[] { 200, 200, 200 },
                Tex = w.Int("tex", -1),
            };
            foreach (var p in w.Array("parts"))
                if (p.ValueKind == JsonValueKind.String) wear.Parts.Add(p.GetString()!);
            look.Wear.Add(wear);
        }
        return look;
    }

    private static byte[]? Rgb(JsonElement e, string key)
    {
        int[]? v = e.Ints(key);
        return v is { Length: >= 3 } ? new[] { (byte)Math.Clamp(v[0], 0, 255), (byte)Math.Clamp(v[1], 0, 255), (byte)Math.Clamp(v[2], 0, 255) } : null;
    }
}

/// <summary>Writes the messages the client sends, one JSON object each.</summary>
public static class Out
{
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    public static string PlayerMove(string pawn, float x, float z, float yaw) =>
        $"{{\"t\":\"player_move\",\"pawn\":{Q(pawn)},\"x\":{F(x)},\"z\":{F(z)},\"yaw\":{F(yaw)}}}";

    public static string PlayerShot(string pawn, string weapon, float[] from, float[] to, string? target, string? part)
    {
        var sb = new StringBuilder();
        sb.Append($"{{\"t\":\"player_shot\",\"pawn\":{Q(pawn)},\"weapon\":{Q(weapon)},");
        sb.Append($"\"from\":[{F(from[0])},{F(from[1])},{F(from[2])}],\"to\":[{F(to[0])},{F(to[1])},{F(to[2])}]");
        if (target != null) sb.Append($",\"target\":{Q(target)},\"part\":{Q(part ?? "body")}");
        return sb.Append('}').ToString();
    }

    public static string PlayerStruck(string pawn, string shot, string part) =>
        $"{{\"t\":\"player_struck\",\"pawn\":{Q(pawn)},\"shot\":{Q(shot)},\"part\":{Q(part)}}}";

    public static string PlayerMelee(string pawn, string target) =>
        $"{{\"t\":\"player_melee\",\"pawn\":{Q(pawn)},\"target\":{Q(target)}}}";

    public static string PlayerThrow(string pawn, float x, float z) =>
        $"{{\"t\":\"player_throw\",\"pawn\":{Q(pawn)},\"to\":[{F(x)},{F(z)}]}}";

    public static string Use(string pawn) => $"{{\"t\":\"use\",\"pawn\":{Q(pawn)}}}";

    public static string Spectate(float x, float z) => $"{{\"t\":\"spectate\",\"x\":{F(x)},\"z\":{F(z)}}}";

    public static string RimworldShown(string back) => $"{{\"t\":\"rimworld_shown\",\"back\":{Q(back)}}}";

    public static string Menu(bool open) => $"{{\"t\":\"menu\",\"open\":{(open ? "true" : "false")}}}";

    public static string LootTake(string pawn, string item) => $"{{\"t\":\"loot_take\",\"pawn\":{Q(pawn)},\"item\":{Q(item)}}}";

    public static string LootDrop(string pawn, string item) => $"{{\"t\":\"loot_drop\",\"pawn\":{Q(pawn)},\"item\":{Q(item)}}}";

    /// <summary>Where RimWorld's window goes as the minimap: pixels into ours.</summary>
    public static string Minimap(float x, float y, float size, bool shown, bool round, float zoom) =>
        $"{{\"t\":\"minimap\",\"x\":{F(x)},\"y\":{F(y)},\"size\":{F(size)},\"shown\":{(shown ? "true" : "false")},\"round\":{(round ? "true" : "false")},\"zoom\":{F(zoom)}}}";
}
