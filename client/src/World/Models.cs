using RimFps.Net;

namespace RimFps.World;

/// <summary>
/// How each of RimWorld's buildings stands in the world. Boomer shooters keep it simple: a
/// box wearing RimWorld's picture on top (beds, tables, benches, machines), or the picture
/// itself standing up as a sprite that turns to face you (lamps, torches, statues), the way
/// Doom stands its lamps and barrels.
/// </summary>
public static class Models
{
    public static bool Has(string def, string word) => def.Contains(word, StringComparison.OrdinalIgnoreCase);

    public static bool IsShelf(string def) => Has(def, "Shelf") || Has(def, "Bookcase");

    /// <summary>Drawn as its picture standing up rather than as a box: whatever RimWorld sends a
    /// cut-out <c>card</c> picture for (lamps, torches, braziers, statues, instruments...).</summary>
    public static bool Standing(Building b) => b.Card >= 0;

    /// <summary>Hangs on the wall it faces away from, rather than standing on the floor.</summary>
    public static bool OnWall(Building b) => Has(b.Def, "WallLamp") || Has(b.Def, "Sconce") || Has(b.Def, "WallTorch");

    /// <summary>How tall a building's box is, in cells.</summary>
    public static float Height(Building b)
    {
        string d = b.Def;
        if (Has(d, "SleepingSpot") || Has(d, "Spot") || Has(d, "Trap") && !Has(d, "Spike")) return 0.03f;
        if (Has(d, "SpikeTrap")) return 0.2f;
        if (Has(d, "Bedroll")) return 0.12f;
        if (Has(d, "Bed") || Has(d, "Cot") || Has(d, "Sarcophagus") || Has(d, "Grave")) return 0.45f;
        if (Has(d, "Table") && !Has(d, "EndTable")) return 0.72f;
        if (Has(d, "EndTable") || Has(d, "Dresser")) return 0.62f;
        if (Has(d, "Chair") || Has(d, "Stool") || Has(d, "Couch") || Has(d, "Armchair") || Has(d, "Pew")) return 0.48f;
        if (IsShelf(d)) return 1.55f;
        if (Has(d, "Sandbag") || Has(d, "Barricade")) return Dims.CoverHeight;
        if (Has(d, "Fence")) return 0.85f;
        if (Has(d, "Hydroponic")) return 0.75f;
        if (Has(d, "Planter")) return 0.4f;
        if (Has(d, "Column")) return Dims.WallHeight;
        if (Has(d, "Turret")) return 1.0f;
        if (Has(d, "Battery")) return 0.9f;
        if (Has(d, "Solar")) return 0.6f;
        if (Has(d, "WindTurbine")) return 1.6f;
        if (Has(d, "Cooler") || Has(d, "Vent")) return Dims.WallHeight;
        if (Has(d, "Heater")) return 1.0f;
        if (Has(d, "Conduit")) return 0.02f;
        if (b.Solid) return Math.Max(Dims.MachineHeight, b.Fill * 1.3f);
        if (b.Fill > 0f) return Math.Clamp(b.Fill * 1.2f, 0.08f, 1.4f);
        return 0.03f;
    }

    /// <summary>Where a grower's plants stand, in cells up: 0 for anything but a grower.</summary>
    public static float SoilTop(string def)
    {
        if (Has(def, "Hydroponic")) return 0.75f;
        if (Has(def, "Planter")) return 0.4f;
        if (Has(def, "PlantPot")) return 0.42f;
        return 0f;
    }

    /// <summary>How tall a standing picture stands, in cells: the long side of how RimWorld draws it.</summary>
    public static float StandingHeight(Building b)
    {
        float tall = Math.Max(b.Draw[0], b.Draw[1]);
        if (Has(b.Def, "Lamp") && !OnWall(b)) return Math.Clamp(tall * 1.1f, 0.8f, 1.6f);
        if (OnWall(b)) return 0.55f;
        return Math.Clamp(tall, 0.4f, 2.6f);
    }
}
