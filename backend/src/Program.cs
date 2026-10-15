using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace RimFps.Backend;

/// <summary>
/// RimWorld FPS Mode's backend: a headless local process that takes the heavy work off
/// RimWorld (map processing, 3D geometry, caches; later physics, bullets, line of sight and
/// weapon analysis). The mod connects on 127.0.0.1:47821 and shares big data through the
/// <see cref="Arena"/>. <c>--parent PID</c> closes it with RimWorld; <c>--selftest</c> builds a
/// made-up map, caches it, loads it back and prints the timings.
/// </summary>
public static class Program
{
    public const int Port = 47821;

    public static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest();
        int parent = 0;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--parent") int.TryParse(args[i + 1], out parent);
        using var arena = new Arena();
        var cache = new MapCache();
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        Console.WriteLine($"[backend] listening on 127.0.0.1:{Port}, arena {arena.Size >> 20} MB");
        if (parent > 0) new Thread(() => WatchParent(parent)) { IsBackground = true }.Start();
        while (true)
        {
            using TcpClient client = listener.AcceptTcpClient();
            client.NoDelay = true;
            try
            {
                Serve(client.GetStream(), arena, cache);
            }
            catch (Exception e) when (e is IOException or SocketException or InvalidDataException)
            {
                Console.WriteLine("[backend] connection lost: " + e.Message);
            }
        }
    }

    private static void WatchParent(int pid)
    {
        while (true)
        {
            Thread.Sleep(2000);
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited) continue;
            }
            catch (ArgumentException)
            {
            }
            Environment.Exit(0);
        }
    }

    /// <summary>One connection: maps in, chunk meshes out.</summary>
    private static void Serve(Stream s, Arena arena, MapCache cache)
    {
        string key = "";
        bool home = false;
        int width = 0, height = 0;
        Cell[]? cells = null;
        while (Frames.Read(s) is var (type, payload))
        {
            switch (type)
            {
                case Msg.MapBegin:
                {
                    // saveId (u16 length + UTF-8), mapId i32, width i32, height i32, modsHash u32, home u8
                    int n = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                    string save = Encoding.UTF8.GetString(payload, 2, n);
                    var rest = payload.AsSpan(2 + n);
                    int mapId = BinaryPrimitives.ReadInt32LittleEndian(rest);
                    width = BinaryPrimitives.ReadInt32LittleEndian(rest[4..]);
                    height = BinaryPrimitives.ReadInt32LittleEndian(rest[8..]);
                    uint mods = BinaryPrimitives.ReadUInt32LittleEndian(rest[12..]);
                    home = rest[16] != 0;
                    key = MapCache.Key(save, mapId, mods);
                    cells = cache.Load(key, home, width, height);
                    if (cells != null) SendMap(s, arena, cells, width, height);
                    break;
                }
                case Msg.MapCells:
                {
                    // offset i64 into the arena where the mod wrote width*height cells
                    long at = BinaryPrimitives.ReadInt64LittleEndian(payload);
                    cells = new Cell[width * height];
                    arena.Read(at, cells);
                    cache.Save(key, home, cells);
                    SendMap(s, arena, cells, width, height);
                    break;
                }
                case Msg.Ping:
                    Frames.Write(s, Msg.Ping, payload);
                    break;
            }
        }
    }

    /// <summary>Builds every chunk (all cores), writes each into the arena, tells the mod.</summary>
    private static void SendMap(Stream s, Arena arena, Cell[] cells, int width, int height)
    {
        arena.Reset();
        var chunks = Mesher.BuildAll(cells, width, height);
        Span<byte> note = stackalloc byte[24];
        foreach (ChunkMesh c in chunks)
        {
            long at = arena.Alloc((long)c.Positions.Length * 24 + (long)c.Indices.Length * 4);
            arena.Write(at, c.Positions);
            arena.Write(at + c.Positions.Length * 12L, c.Uvs);
            arena.Write(at + c.Positions.Length * 24L, c.Indices);
            BinaryPrimitives.WriteInt32LittleEndian(note, c.ChunkX);
            BinaryPrimitives.WriteInt32LittleEndian(note[4..], c.ChunkZ);
            BinaryPrimitives.WriteInt64LittleEndian(note[8..], at);
            BinaryPrimitives.WriteInt32LittleEndian(note[16..], c.Positions.Length);
            BinaryPrimitives.WriteInt32LittleEndian(note[20..], c.Indices.Length);
            Frames.Write(s, Msg.ChunkReady, note);
        }
        Frames.Write(s, Msg.MapReady, ReadOnlySpan<byte>.Empty);
    }

    private static int SelfTest()
    {
        const int w = 250, h = 250;
        var rng = new Random(1);
        var cells = new Cell[w * h];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new Cell { Solid = (byte)(rng.NextDouble() < 0.18 ? 1 : 0), Roofed = (byte)(rng.NextDouble() < 0.3 ? 1 : 0), WallTex = 3, FloorTex = 7 };
        var clock = Stopwatch.StartNew();
        var chunks = Mesher.BuildAll(cells, w, h);
        double build = clock.Elapsed.TotalMilliseconds;
        long verts = chunks.Sum(c => (long)c.Positions.Length), tris = chunks.Sum(c => (long)c.Indices.Length) / 3;
        string dir = Path.Combine(Path.GetTempPath(), "rimfps-selftest");
        var cache = new MapCache(dir);
        string key = MapCache.Key("Test Save", 0, 0xC0FFEE);
        clock.Restart();
        cache.Save(key, home: true, cells);
        double save = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        Cell[]? back = cache.Load(key, home: true, w, h);
        double load = clock.Elapsed.TotalMilliseconds;
        bool same = back != null && MemoryMarshal.AsBytes(back.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(cells.AsSpan()));
        using var arena = new Arena(64L << 20, Path.Combine(dir, "arena.bin"));
        clock.Restart();
        foreach (ChunkMesh c in chunks)
        {
            long at = arena.Alloc((long)c.Positions.Length * 24 + (long)c.Indices.Length * 4);
            arena.Write(at, c.Positions);
            arena.Write(at + c.Positions.Length * 12L, c.Uvs);
            arena.Write(at + c.Positions.Length * 24L, c.Indices);
        }
        double share = clock.Elapsed.TotalMilliseconds;
        var touched = Mesher.Touched(new[] { 125 * w + 125 }, w);
        Console.WriteLine($"map {w}x{h}: {chunks.Length} chunks, {verts:N0} vertices, {tris:N0} triangles");
        Console.WriteLine($"build {build:0.0} ms (all cores), cache save {save:0.0} ms, cache load {load:0.0} ms, round trip {(same ? "identical" : "MISMATCH")}");
        Console.WriteLine($"into shared memory {share:0.0} ms; one changed cell rebuilds {touched.Count} chunk(s)");
        return same ? 0 : 1;
    }
}
