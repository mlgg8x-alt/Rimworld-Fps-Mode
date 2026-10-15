using System.Buffers.Binary;
using System.Net.Sockets;

namespace RimFps.Backend;

/// <summary>
/// The backend's wire format with the mod: small binary frames over TCP for control
/// (<c>[u32 length][u16 type][payload]</c>, little endian), big data in the shared
/// <see cref="Arena"/>, referred to by offset and length. No text, no JSON: nothing for
/// RimWorld's garbage collector to chew on.
/// </summary>
public enum Msg : ushort
{
    /// <summary>Mod to backend: version, RimWorld's process id.</summary>
    Hello = 1,
    /// <summary>Mod to backend: a map is coming. saveId, mapId, width, height, modsHash, home (1/0).</summary>
    MapBegin = 2,
    /// <summary>Mod to backend: cells, row-major, one <see cref="Cell"/> each, in the arena.</summary>
    MapCells = 3,
    /// <summary>Mod to backend: these cells changed (index list + new cells, in the arena).</summary>
    CellsChanged = 4,
    /// <summary>Backend to mod: a chunk's mesh is ready in the arena (chunk x, z, offset, vertex count, index count).</summary>
    ChunkReady = 10,
    /// <summary>Backend to mod: every chunk of the map is ready (from cache or built).</summary>
    MapReady = 11,
    Ping = 20,
}

/// <summary>One map cell as the mod packs it: 8 bytes.</summary>
public struct Cell
{
    /// <summary>0 open, 1 wall or rock, 2 door, 3 water (not walkable).</summary>
    public byte Solid;
    public byte Roofed;
    /// <summary>Index into the mod's texture table for the wall, the floor.</summary>
    public ushort WallTex, FloorTex;
    public ushort Flags;
}

public static class Frames
{
    public static void Write(Stream s, Msg type, ReadOnlySpan<byte> payload)
    {
        Span<byte> head = stackalloc byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)(payload.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(head[4..], (ushort)type);
        s.Write(head);
        s.Write(payload);
    }

    /// <summary>The next frame, or null when the connection closed.</summary>
    public static (Msg type, byte[] payload)? Read(Stream s)
    {
        Span<byte> head = stackalloc byte[6];
        if (!Fill(s, head)) return null;
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(head) - 2;
        var type = (Msg)BinaryPrimitives.ReadUInt16LittleEndian(head[4..]);
        if (length < 0 || length > 16 << 20) throw new InvalidDataException($"frame of {length} bytes");
        var payload = new byte[length];
        return Fill(s, payload) ? (type, payload) : null;
    }

    private static bool Fill(Stream s, Span<byte> into)
    {
        int got = 0;
        while (got < into.Length)
        {
            int n = s.Read(into[got..]);
            if (n == 0) return false;
            got += n;
        }
        return true;
    }
}
