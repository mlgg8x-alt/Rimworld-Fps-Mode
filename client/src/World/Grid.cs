using RimFps.Net;

namespace RimFps.World;

/// <summary>Sizes in the client's world, in RimWorld cells: one unit is one cell across.</summary>
public static class Dims
{
    /// <summary>A person stands this tall; the player's eye is a little under the top.</summary>
    public const float PersonHeight = 0.95f;
    public const float Eye = 0.80f;
    public const float CrouchEye = 0.50f;
    /// <summary>Walls, built roofs and cave ceilings: roomy, as a boomer shooter's rooms are.</summary>
    public const float WallHeight = 2.0f;
    /// <summary>The top of a doorway; the lintel fills the rest up to the wall's top.</summary>
    public const float DoorHeight = 1.45f;
    /// <summary>Natural rock rises at least this high, more inside a mountain.</summary>
    public const float RockHeight = 2.4f;
    public const float RockRise = 0.75f;
    /// <summary>The most a mountain rises over <see cref="RockHeight"/>, by the world tile's
    /// hilliness: unknown, flat, small hills, large hills, mountainous, impassable.</summary>
    public static readonly float[] RiseLimit = { 5f, 1f, 2f, 4f, 7f, 10f };
    /// <summary>A machine or turret with nothing better to show is a box this tall.</summary>
    public const float MachineHeight = 1.25f;
    /// <summary>Cover (sandbags, barricades) stops bullets up to this high.</summary>
    public const float CoverHeight = 0.55f;
    /// <summary>The player's width, for walls, and for bullets.</summary>
    public const float PlayerRadius = 0.24f;
    public const float BodyHalfWidth = 0.21f;
    /// <summary>Shot heights from the mod are in MW2 blocks: half a cell each.</summary>
    public const float ShotHeightScale = 0.5f;
}

/// <summary>RimWorld's terrain codes (protocol/PROTOCOL.md).</summary>
public static class Code
{
    public const byte Soil = (byte)'.', Sand = (byte)',', Stone = (byte)':', Floor = (byte)'=';
    public const byte Shallow = (byte)'~', Deep = (byte)'O', Rock = (byte)'#', Wall = (byte)'W';
    public const byte Door = (byte)'+', Cover = (byte)'c', Machine = (byte)'M', Tree = (byte)'T', Grass = (byte)'g';
}

/// <summary>
/// The map as RimWorld has it, cell by cell, in terrain order (row by row from the south
/// edge, west to east: cell (x, z) is index z * W + x), with what the client builds from it:
/// mountain heights, door swings and the buildings standing on each cell.
/// </summary>
public sealed class Grid
{
    public int W, H;
    public byte[] Codes = Array.Empty<byte>();
    public byte[] Roofs = Array.Empty<byte>();
    public int[] WallSkin = Array.Empty<int>();
    public int[] FloorSkin = Array.Empty<int>();
    /// <summary>RimWorld's path cost of walking into a cell; -1 unknown, 1001 impassable.</summary>
    public short[] Costs = Array.Empty<short>();
    /// <summary>Lamp light, 0 to 15 (RimWorld's glow grid without daylight).</summary>
    public byte[] Glow = Array.Empty<byte>();
    public bool HasGlow;
    /// <summary>Rock's height, from the mountain's edge; 0 elsewhere.</summary>
    public float[] RockTop = Array.Empty<float>();
    /// <summary>The top of whatever is built on a cell (a bed, a shelf), for what lies on it.</summary>
    public float[] BuiltTop = Array.Empty<float>();
    /// <summary>Building heights that stop the player (machines RimWorld calls impassable).</summary>
    public bool[] Shelf = Array.Empty<bool>();
    public int Hills;
    /// <summary>How far each door has swung, 0 shut to 1 open, by cell index; absent means
    /// shut once RimWorld has sent any doors.</summary>
    public readonly Dictionary<int, float> DoorOpen = new();
    public readonly Dictionary<int, DoorState> DoorAt = new();
    public bool HasDoors;

    public int Revision;
    private int terrainRev = -1, roofsRev = -1, skinsRev = -1, costsRev = -1, glowRev = -1, doorsRev = -1, buildingsRev = -1;
    /// <summary>What changed since the meshes were built.</summary>
    public bool ShapeDirty, LightDirty, DoorsDirty;

    public bool Ready => W > 0 && Codes.Length == W * H;

    public bool Inside(int x, int z) => x >= 0 && z >= 0 && x < W && z < H;

    public int Index(int x, int z) => z * W + x;

    public byte CodeAt(int x, int z) => Inside(x, z) ? Codes[Index(x, z)] : Code.Rock;

    public byte RoofAt(int x, int z) => Inside(x, z) ? Roofs[Index(x, z)] : (byte)'.';

    /// <summary>Takes whatever RimWorld changed since last time. True when anything did.</summary>
    public bool Update(Link link)
    {
        bool changed = false;
        if (link.MapW > 0 && link.MapH > 0 && (link.MapW != W || link.MapH != H))
        {
            Resize(link.MapW, link.MapH);
            terrainRev = roofsRev = skinsRev = costsRev = glowRev = doorsRev = buildingsRev = -1;
            changed = true;
        }
        if (!Ready && W == 0) return false;
        if (link.TerrainRev != terrainRev)
        {
            terrainRev = link.TerrainRev;
            if (link.Terrain is string cells && cells.Length >= W * H)
                for (int i = 0; i < W * H; i++) Codes[i] = cells[i] < 128 ? (byte)cells[i] : Code.Soil;
            ShapeDirty = changed = true;
        }
        if (link.RoofsRev != roofsRev)
        {
            roofsRev = link.RoofsRev;
            Hills = link.Hills;
            if (link.Roofs is string cells && cells.Length >= W * H)
                for (int i = 0; i < W * H; i++) Roofs[i] = cells[i] < 128 ? (byte)cells[i] : (byte)'.';
            ShapeDirty = changed = true;
        }
        if (link.SkinsRev != skinsRev)
        {
            skinsRev = link.SkinsRev;
            Skins(link.WallSkins, WallSkin);
            Skins(link.FloorSkins, FloorSkin);
            ShapeDirty = changed = true;
        }
        if (link.CostsRev != costsRev)
        {
            costsRev = link.CostsRev;
            if (link.Costs is string cells && cells.Length >= W * H)
                for (int i = 0; i < W * H; i++) Costs[i] = (short)Math.Clamp(cells[i] - '0', 0, 1001);
            changed = true;
        }
        if (link.GlowRev != glowRev)
        {
            glowRev = link.GlowRev;
            if (link.Glow is string cells && cells.Length >= W * H)
            {
                HasGlow = true;
                for (int i = 0; i < W * H; i++) Glow[i] = (byte)Hex(cells[i]);
            }
            LightDirty = changed = true;
        }
        if (link.DoorsRev != doorsRev)
        {
            doorsRev = link.DoorsRev;
            DoorOpen.Clear();
            DoorAt.Clear();
            HasDoors = true;
            foreach (DoorState door in link.Doors)
            {
                if (!Inside(door.X, door.Z)) continue;
                int i = Index(door.X, door.Z);
                DoorOpen[i] = Math.Clamp(door.Open / (float)link.DoorSteps, 0f, 1f);
                DoorAt[i] = door;
            }
            DoorsDirty = changed = true;
        }
        if (link.BuildingsRev != buildingsRev)
        {
            buildingsRev = link.BuildingsRev;
            Array.Clear(BuiltTop);
            Array.Clear(Shelf);
            foreach (Building b in link.Buildings)
            {
                float top = Models.Height(b);
                bool shelf = Models.IsShelf(b.Def);
                for (int z = b.Rect[1]; z <= b.Rect[3]; z++)
                    for (int x = b.Rect[0]; x <= b.Rect[2]; x++)
                    {
                        if (!Inside(x, z)) continue;
                        int i = Index(x, z);
                        BuiltTop[i] = Math.Max(BuiltTop[i], Models.Standing(b) ? 0f : top);
                        if (shelf) Shelf[i] = true;
                    }
            }
            changed = true;
        }
        if (ShapeDirty) Mountains();
        if (changed) Revision++;
        return changed;
    }

    private void Resize(int w, int h)
    {
        W = w;
        H = h;
        int n = w * h;
        Codes = new byte[n];
        Array.Fill(Codes, Code.Soil);
        Roofs = new byte[n];
        Array.Fill(Roofs, (byte)'.');
        WallSkin = new int[n];
        Array.Fill(WallSkin, -1);
        FloorSkin = new int[n];
        Array.Fill(FloorSkin, -1);
        Costs = new short[n];
        Array.Fill(Costs, (short)-1);
        Glow = new byte[n];
        RockTop = new float[n];
        BuiltTop = new float[n];
        Shelf = new bool[n];
        DoorOpen.Clear();
        DoorAt.Clear();
        HasDoors = HasGlow = false;
        ShapeDirty = LightDirty = DoorsDirty = true;
    }

    private void Skins(string? cells, int[] into)
    {
        Array.Fill(into, -1);
        if (cells == null || cells.Length < W * H) return;
        for (int i = 0; i < W * H; i++) into[i] = cells[i] == '.' ? -1 : cells[i] - '0';
    }

    private static int Hex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };

    /// <summary>RimWorld keeps no height for its rock, only where it is and its roofs: a
    /// mountain rises with the distance in from its edge, as high as the world tile is hilly.</summary>
    private void Mountains()
    {
        int n = W * H;
        var distance = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            bool mass = Codes[i] == Code.Rock || Roofs[i] is (byte)'r' or (byte)'R';
            distance[i] = mass ? int.MaxValue : 0;
            if (!mass) queue.Enqueue(i);
        }
        // Past the map's edge counts as more mountain, so edge mountains keep rising.
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % W, z = i / W;
            Span<int> next = stackalloc int[] { x - 1, z, x + 1, z, x, z - 1, x, z + 1 };
            for (int k = 0; k < 8; k += 2)
            {
                int nx = next[k], nz = next[k + 1];
                if (!Inside(nx, nz)) continue;
                int j = Index(nx, nz);
                if (distance[j] > distance[i] + 1)
                {
                    distance[j] = distance[i] + 1;
                    queue.Enqueue(j);
                }
            }
        }
        float limit = Dims.RiseLimit[Math.Clamp(Hills, 0, Dims.RiseLimit.Length - 1)];
        for (int i = 0; i < n; i++)
        {
            int d = distance[i] == int.MaxValue ? 8 : distance[i];
            RockTop[i] = d > 0 ? Dims.RockHeight + Math.Min((d - 1) * Dims.RockRise, limit) : 0f;
        }
    }

    /// <summary>How far the door on cell (x, z) has swung, 0 to 1; shut when RimWorld has not said.</summary>
    public float DoorSwing(int x, int z) =>
        !HasDoors ? 1f : DoorOpen.TryGetValue(Index(x, z), out float open) ? open : 0f;

    /// <summary>Whether the player can not stand in cell (x, z).</summary>
    public bool Blocks(int x, int z)
    {
        if (!Inside(x, z)) return true;
        int i = Index(x, z);
        switch (Codes[i])
        {
            case Code.Rock:
            case Code.Wall:
            case Code.Machine:
            case Code.Deep:
                return true;
            case Code.Door:
                return DoorSwing(x, z) < 0.5f;
        }
        return Costs[i] >= 1001;
    }

    /// <summary>How tall the solid part of cell (x, z) is, for bullets; 0 where they fly
    /// through. Doors that stand open leave only their lintel.</summary>
    public float SolidTop(int x, int z, out float bottom)
    {
        bottom = 0f;
        if (!Inside(x, z)) return 100f;
        int i = Index(x, z);
        switch (Codes[i])
        {
            case Code.Rock: return Math.Max(RockTop[i], Dims.RockHeight);
            case Code.Wall: return Dims.WallHeight;
            case Code.Machine: return Math.Max(BuiltTop[i], Dims.MachineHeight);
            case Code.Door:
                if (DoorSwing(x, z) < 0.5f) return Dims.WallHeight;
                bottom = Dims.DoorHeight;
                return Dims.WallHeight;
        }
        return 0f;
    }

    /// <summary>Whether a point at height y over cell (x, z) is inside something solid, as
    /// RimWorld's bullets meet it: walls, rock, machines, door leaves and lintels, roofs over
    /// it and, low down, cover.</summary>
    public bool Stops(int x, int z, float y, bool cover)
    {
        if (!Inside(x, z)) return true;
        if (y <= 0f) return true;
        float top = SolidTop(x, z, out float bottom);
        if (top > 0f && y >= bottom && y <= top) return true;
        int i = Index(x, z);
        if (cover && Codes[i] == Code.Cover && y <= Dims.CoverHeight) return true;
        byte roof = Roofs[i];
        return roof != (byte)'.' && y >= Dims.WallHeight && y <= Dims.WallHeight + 0.3f;
    }

    /// <summary>RimWorld's path cost of walking into cell (x, z), or a guess before it says.</summary>
    public float Cost(int x, int z)
    {
        if (!Inside(x, z)) return 0f;
        int i = Index(x, z);
        if (Costs[i] >= 0) return Math.Min(Costs[i], (short)1000);
        return Codes[i] == Code.Tree ? 42f : 0f;
    }

    /// <summary>How much of the sky reaches cell (x, z): all of it outdoors, some through the
    /// doors and windows of a built room, little in a cave.</summary>
    public float Outdoors(int x, int z)
    {
        byte roof = RoofAt(x, z);
        return roof switch
        {
            (byte)'.' => 1f,
            (byte)'c' => 0.28f,
            _ => 0.10f,
        };
    }

    /// <summary>The lamps' light on cell (x, z), 0 to 1, with a floor under a roof so a room
    /// with no lamp is dark but not black.</summary>
    public float Lamp(int x, int z)
    {
        if (!Inside(x, z)) return 0f;
        int i = Index(x, z);
        float lit = HasGlow ? Glow[i] / 15f : 0f;
        float floor = Roofs[i] switch { (byte)'.' => 0f, (byte)'c' => 0.22f, _ => 0.12f };
        if (!HasGlow && Roofs[i] != (byte)'.') floor = Roofs[i] == (byte)'c' ? 0.6f : 0.35f;
        return Math.Max(lit, floor);
    }

    /// <summary>How lit cell (x, z) is now, by the sky's light and its lamps: 0 to 1.</summary>
    public float Light(int x, int z, float sky) => Math.Max(Lamp(x, z), Outdoors(x, z) * sky);
}
