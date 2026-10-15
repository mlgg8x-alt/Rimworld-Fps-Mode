using System.Numerics;

namespace RimFps.Backend;

/// <summary>A chunk's geometry, ready for the GPU: positions, UVs (with the texture index in z),
/// triangle indices. World space, RimWorld cells: x east, y up, z north.</summary>
public sealed class ChunkMesh
{
    public int ChunkX, ChunkZ;
    public Vector3[] Positions = Array.Empty<Vector3>();
    public Vector3[] Uvs = Array.Empty<Vector3>();
    public int[] Indices = Array.Empty<int>();
}

/// <summary>
/// Builds the 3D world from RimWorld's cells, 16 by 16 cells a chunk, on any number of
/// threads (RimWorld uses about one core; the rest are free): a floor quad per open cell, wall
/// faces only where a wall meets open ground, a ceiling under roofs. Only changed chunks are
/// rebuilt.
/// </summary>
public static class Mesher
{
    public const int ChunkSize = 16;
    public const float WallHeight = 2.4f;

    public static ChunkMesh[] BuildAll(Cell[] cells, int width, int height)
    {
        int cx = (width + ChunkSize - 1) / ChunkSize, cz = (height + ChunkSize - 1) / ChunkSize;
        var chunks = new ChunkMesh[cx * cz];
        Parallel.For(0, chunks.Length, i => chunks[i] = Build(cells, width, height, i % cx, i / cx));
        return chunks;
    }

    public static ChunkMesh Build(Cell[] cells, int width, int height, int chunkX, int chunkZ)
    {
        var pos = new List<Vector3>();
        var uv = new List<Vector3>();
        var idx = new List<int>();
        bool Open(int x, int z) => x < 0 || z < 0 || x >= width || z >= height ? false : cells[z * width + x].Solid != 1;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, ushort tex, float u = 1, float v = 1)
        {
            int n = pos.Count;
            pos.Add(a); pos.Add(b); pos.Add(c); pos.Add(d);
            uv.Add(new(0, v, tex)); uv.Add(new(u, v, tex)); uv.Add(new(u, 0, tex)); uv.Add(new(0, 0, tex));
            idx.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        int x0 = chunkX * ChunkSize, z0 = chunkZ * ChunkSize;
        for (int z = z0; z < Math.Min(z0 + ChunkSize, height); z++)
            for (int x = x0; x < Math.Min(x0 + ChunkSize, width); x++)
            {
                Cell c = cells[z * width + x];
                if (c.Solid != 1)
                {
                    Quad(new(x, 0, z), new(x + 1, 0, z), new(x + 1, 0, z + 1), new(x, 0, z + 1), c.FloorTex);
                    if (c.Roofed != 0)
                        Quad(new(x, WallHeight, z + 1), new(x + 1, WallHeight, z + 1), new(x + 1, WallHeight, z), new(x, WallHeight, z), 0);
                    continue;
                }
                float h = WallHeight;
                // A face only where this wall meets open ground.
                if (Open(x, z - 1)) Quad(new(x + 1, 0, z), new(x, 0, z), new(x, h, z), new(x + 1, h, z), c.WallTex);
                if (Open(x, z + 1)) Quad(new(x, 0, z + 1), new(x + 1, 0, z + 1), new(x + 1, h, z + 1), new(x, h, z + 1), c.WallTex);
                if (Open(x - 1, z)) Quad(new(x, 0, z), new(x, 0, z + 1), new(x, h, z + 1), new(x, h, z), c.WallTex);
                if (Open(x + 1, z)) Quad(new(x + 1, 0, z + 1), new(x + 1, 0, z), new(x + 1, h, z), new(x + 1, h, z + 1), c.WallTex);
                Quad(new(x, h, z), new(x + 1, h, z), new(x + 1, h, z + 1), new(x, h, z + 1), c.WallTex);
            }
        return new ChunkMesh { ChunkX = chunkX, ChunkZ = chunkZ, Positions = pos.ToArray(), Uvs = uv.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>The chunks a set of changed cells touches (a wall's faces reach its neighbours).</summary>
    public static HashSet<(int, int)> Touched(IEnumerable<int> changed, int width)
    {
        var set = new HashSet<(int, int)>();
        foreach (int i in changed)
        {
            int x = i % width, z = i / width;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (x + dx >= 0 && z + dz >= 0) set.Add(((x + dx) / ChunkSize, (z + dz) / ChunkSize));
        }
        return set;
    }
}
