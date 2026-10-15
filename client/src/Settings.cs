using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimFps;

/// <summary>Where one HUD widget sits: its top left corner as a share of the screen's width
/// and height, its size (1 is the default size at 1080 lines), and whether it shows. The
/// minimap also keeps whether it is round and how far out it looks.</summary>
public sealed class Widget
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Scale { get; set; } = 1f;
    public bool Shown { get; set; } = true;
    public bool Round { get; set; }
    public float Zoom { get; set; } = 1f;

    public Widget Copy() => (Widget)MemberwiseClone();
}

/// <summary>How a weapon is held, set in the HUD's edit mode: where its grip sits in front of
/// the eye (x right, y up, -z ahead) and how it is turned, at the hip and when aiming; its
/// size and thickness against the automatic ones; and where the hands and the muzzle are on
/// its picture (u from its back end, v up from its bottom, 0 to 1; below 0 means found
/// automatically from its outline).</summary>
public sealed class WeaponPose
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public float AimX { get; set; }
    public float AimY { get; set; }
    public float AimZ { get; set; }
    public float AimYaw { get; set; }
    public float AimPitch { get; set; }
    public float AimRoll { get; set; }
    public float Size { get; set; } = 1f;
    public float Depth { get; set; } = 1f;
    public float HandSize { get; set; } = 1f;
    /// <summary>0 automatic, 1 one hand, 2 two hands.</summary>
    public int HandCount { get; set; }
    public float GripU { get; set; } = -1f;
    public float GripV { get; set; } = -1f;
    public float ForeU { get; set; } = -1f;
    public float ForeV { get; set; } = -1f;
    public float MuzzleU { get; set; } = -1f;
    public float MuzzleV { get; set; } = -1f;

    public WeaponPose Copy() => (WeaponPose)MemberwiseClone();

    /// <summary>A gun: low at the right hip, turned in so its side shows; aimed, under the
    /// middle of the view.</summary>
    public static WeaponPose Gun() => new()
    {
        X = 0.13f, Y = -0.125f, Z = -0.34f, Yaw = 26f, Pitch = 3f, Roll = -4f,
        AimX = 0.04f, AimY = -0.09f, AimZ = -0.36f, AimYaw = 12f, AimPitch = 2f, AimRoll = 0f,
    };

    /// <summary>A melee weapon: raised over the right shoulder, ready to swing.</summary>
    public static WeaponPose Melee() => new()
    {
        X = 0.15f, Y = -0.115f, Z = -0.3f, Yaw = 22f, Pitch = 60f, Roll = -12f,
        AimX = 0.15f, AimY = -0.115f, AimZ = -0.3f, AimYaw = 22f, AimPitch = 60f, AimRoll = -12f,
    };
}

/// <summary>
/// The player's options, saved between runs (in the user's application data folder, as
/// <c>RimWorldFPS/client.json</c>). Environment variables set before starting win over the
/// file for that run: <c>RWMW2_BRIDGE_ADDR</c>, <c>RWMW2_ONE_WINDOW</c> and
/// <c>RWMW2_FULLSCREEN</c> as upstream had them, and <c>RIMFPS_*</c>.
/// </summary>
public sealed class Settings
{
    public string Address { get; set; } = Net.Bridge.DefaultAddress;
    /// <summary><c>rimworld</c>: RimWorld's art at full resolution, smooth light. <c>retro</c>:
    /// chunky pixels, a 256 colour palette and banded light, as a 90s shooter.</summary>
    public string Style { get; set; } = "rimworld";
    /// <summary>Degrees the view turns per pixel the mouse moves, and how much slower aiming
    /// down the sights is.</summary>
    public float Sensitivity { get; set; } = 0.1f;
    public float AdsSensitivity { get; set; } = 0.65f;
    public bool InvertY { get; set; }
    /// <summary>Vertical field of view, in degrees.</summary>
    public float Fov { get; set; } = 72f;
    /// <summary>Retro style only: lines the 3D view is drawn at, its palette, dithering, and
    /// Doom's darkening with distance.</summary>
    public int RenderHeight { get; set; } = 240;
    public string Palette { get; set; } = "ramps";
    public bool Dither { get; set; } = true;
    public bool Diminish { get; set; } = true;
    public float Volume { get; set; } = 0.6f;
    /// <summary>Fill the screen when a colonist is played.</summary>
    public bool Fullscreen { get; set; } = true;
    /// <summary>Hide the window while no colonist is played, and close with RimWorld.</summary>
    public bool OneWindow { get; set; } = true;
    /// <summary>How far, in cells, the view reaches before fog swallows it.</summary>
    public float ViewDistance { get; set; } = 120f;
    public bool Bob { get; set; } = true;
    public bool ShowFps { get; set; }
    public bool HoldToAim { get; set; } = true;
    public Dictionary<string, Widget> Hud { get; set; } = DefaultHud();
    /// <summary>How each weapon is held, by its RimWorld picture; <c>*gun</c> and <c>*melee</c>
    /// for every weapon without its own.</summary>
    public Dictionary<string, WeaponPose> Weapons { get; set; } = new();

    /// <summary>The way the weapon with picture <paramref name="name"/> is held.</summary>
    public WeaponPose PoseFor(string name, bool melee) =>
        Weapons.TryGetValue(name, out WeaponPose? own) ? own
        : Weapons.TryGetValue(melee ? "*melee" : "*gun", out WeaponPose? all) ? all
        : melee ? WeaponPose.Melee() : WeaponPose.Gun();

    [JsonIgnore] public bool Retro => Style == "retro";
    /// <summary>Pixels a RimWorld picture keeps on its long side (RimWorld saves 128).</summary>
    [JsonIgnore] public int PictureTexels => Retro ? 64 : 128;
    /// <summary>Pixels a cell of RimWorld's ground keeps across (RimWorld's own are 32).</summary>
    [JsonIgnore] public int FloorTexels => 32;

    public static Dictionary<string, Widget> DefaultHud() => new()
    {
        ["minimap"] = new Widget { X = 0.012f, Y = 0.02f, Scale = 1f, Round = true, Zoom = 1f },
        ["status"] = new Widget { X = 0.012f, Y = 0.62f },
        ["weapon"] = new Widget { X = 0.80f, Y = 0.83f },
        ["compass"] = new Widget { X = 0.35f, Y = 0.012f },
        ["messages"] = new Widget { X = 0.36f, Y = 0.1f },
        ["prompt"] = new Widget { X = 0.44f, Y = 0.6f },
    };

    [JsonIgnore]
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorldFPS", "client.json");

    public static Settings Load()
    {
        Settings settings = new();
        try
        {
            if (File.Exists(Path))
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path), Options) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[fps] could not read {Path}: {e.Message}; using the defaults");
        }
        settings.FromEnvironment();
        settings.Clamp();
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[fps] could not save {Path}: {e.Message}");
        }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private void FromEnvironment()
    {
        static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v.Trim() : null;
        static bool? Flag(string name) => Env(name)?.ToLowerInvariant() switch
        {
            "0" or "off" or "false" or "no" => false,
            "1" or "on" or "true" or "yes" => true,
            _ => null,
        };
        if (Env("RWMW2_BRIDGE_ADDR") is string address) Address = address;
        if (Env("RIMFPS_ADDR") is string a) Address = a;
        if (Flag("RWMW2_ONE_WINDOW") is bool one) OneWindow = one;
        if (Flag("RIMFPS_ONE_WINDOW") is bool one2) OneWindow = one2;
        if (Flag("RWMW2_FULLSCREEN") is bool full) Fullscreen = full;
        if (Flag("RIMFPS_FULLSCREEN") is bool full2) Fullscreen = full2;
        if (Env("RIMFPS_STYLE") is string style) Style = style;
    }

    public void Clamp()
    {
        Sensitivity = Math.Clamp(Sensitivity, 0.01f, 1f);
        AdsSensitivity = Math.Clamp(AdsSensitivity, 0.1f, 2f);
        Fov = Math.Clamp(Fov, 50f, 110f);
        RenderHeight = Math.Clamp(RenderHeight, 120, 720);
        Volume = Math.Clamp(Volume, 0f, 1f);
        ViewDistance = Math.Clamp(ViewDistance, 30f, 250f);
        if (Palette is not ("ramps" or "ramps64" or "off")) Palette = "ramps";
        if (Style is not ("rimworld" or "retro")) Style = "rimworld";
        Hud ??= DefaultHud();
        foreach (var (key, widget) in DefaultHud())
            if (!Hud.ContainsKey(key)) Hud[key] = widget;
        foreach (Widget w in Hud.Values)
        {
            w.X = Math.Clamp(w.X, 0f, 0.98f);
            w.Y = Math.Clamp(w.Y, 0f, 0.98f);
            w.Scale = Math.Clamp(w.Scale, 0.4f, 3f);
            w.Zoom = Math.Clamp(w.Zoom, 0.3f, 4f);
        }
    }
}
