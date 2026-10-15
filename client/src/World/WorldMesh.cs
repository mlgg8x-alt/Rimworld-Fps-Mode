using System.Numerics;
using RimFps.Net;
using Raylib_cs;

namespace RimFps.World;

/// <summary>
/// The map's static geometry: ground, walls, rock, door lintels, ceilings and the boxes of
/// what is built, in chunks of 16 by 16 cells, one mesh per atlas page. A chunk is built again
/// only when something inside it (or just beside it) changed, or when a picture it was
/// waiting for has loaded.
///
/// Vertex colours carry how a face is lit, not its colour: red is lamp light (0 to 1), green
/// how much of the sky reaches it, blue a fixed shade by which way it faces. The world shader
/// mixes them with the sky's brightness each frame, so dusk falls without rebuilding.
/// </summary>
public sealed class WorldMesh : IDisposable
{
    public const int ChunkSize = 16;

    private sealed class Chunk
    {
        public int Cx, Cz;
        public long Hash = long.MinValue;
        public bool Waiting;
        public readonly List<(int page, Mesh mesh)> Meshes = new();
    }

    private readonly Grid grid;
    private readonly Pictures pictures;
    private readonly Builtins art;
    private Chunk[] chunks = Array.Empty<Chunk>();
    private int cw, ch;
    private int picturesRev = -1;
    private int gridRev = -1;
    private List<Building> buildings = new();
    private int buildingsRev = -1;
    /// <summary>Buildings by the chunk their first cell is in.</summary>
    private readonly Dictionary<int, List<Building>> byChunk = new();

    public int ChunksBuilt { get; private set; }

    public WorldMesh(Grid grid, Pictures pictures, Builtins art)
    {
        this.grid = grid;
        this.pictures = pictures;
        this.art = art;
    }

    /// <summary>Builds the chunks that changed, at most <paramref name="most"/> of them (all
    /// on the first build, so the world appears whole).</summary>
    public void Update(Link link, int most)
    {
        if (!grid.Ready) return;
        int wantW = (grid.W + ChunkSize - 1) / ChunkSize, wantH = (grid.H + ChunkSize - 1) / ChunkSize;
        if (wantW != cw || wantH != ch)
        {
            Clear();
            cw = wantW;
            ch = wantH;
            chunks = new Chunk[cw * ch];
            for (int z = 0; z < ch; z++)
                for (int x = 0; x < cw; x++)
                    chunks[z * cw + x] = new Chunk { Cx = x, Cz = z };
            most = int.MaxValue;
        }
        if (link.BuildingsRev != buildingsRev)
        {
            buildingsRev = link.BuildingsRev;
            buildings = link.Buildings;
            byChunk.Clear();
            foreach (Building b in buildings)
            {
                if (Models.Standing(b)) continue;
                int key = (b.Rect[1] / ChunkSize) * 10000 + b.Rect[0] / ChunkSize;
                if (!byChunk.TryGetValue(key, out var list)) byChunk[key] = list = new List<Building>();
                list.Add(b);
            }
        }
        bool picturesChanged = pictures.Revision != picturesRev;
        picturesRev = pictures.Revision;
        if (grid.Revision == gridRev && !picturesChanged && !grid.ShapeDirty && !grid.LightDirty) return;
        gridRev = grid.Revision;
        int built = 0;
        foreach (Chunk chunk in chunks)
        {
            long hash = Hash(chunk);
            if (hash == chunk.Hash && !(chunk.Waiting && picturesChanged)) continue;
            if (built >= most)
            {
                // Come back next frame for the rest.
                gridRev = -1;
                break;
            }
            Build(chunk);
            chunk.Hash = hash;
            built++;
        }
        ChunksBuilt += built;
        if (built > 0 || most == int.MaxValue)
        {
            grid.ShapeDirty = false;
            grid.LightDirty = false;
        }
    }

    /// <summary>A fingerprint of everything a chunk's meshes are made from.</summary>
    private long Hash(Chunk chunk)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v)
            {
                h ^= v;
                h *= 1099511628211L;
            }
            int x0 = chunk.Cx * ChunkSize - 1, z0 = chunk.Cz * ChunkSize - 1;
            for (int z = z0; z <= z0 + ChunkSize + 1; z++)
                for (int x = x0; x <= x0 + ChunkSize + 1; x++)
                {
                    if (!grid.Inside(x, z))
                    {
                        Mix(-1);
                        continue;
                    }
                    int i = grid.Index(x, z);
                    Mix(grid.Codes[i]);
                    Mix(grid.Roofs[i]);
                    Mix(grid.WallSkin[i]);
                    Mix(grid.FloorSkin[i]);
                    Mix(grid.Glow[i]);
                    Mix((long)(grid.RockTop[i] * 100));
                    Mix((long)(grid.BuiltTop[i] * 100));
                }
            if (byChunk.TryGetValue(chunk.Cz * 10000 + chunk.Cx, out var list))
                foreach (Building b in list)
                {
                    Mix(b.Def.GetHashCode());
                    Mix(b.Rect[0] * 7 + b.Rect[1] * 13 + b.Rect[2] * 17 + b.Rect[3] * 19);
                    Mix(b.Tex);
                    Mix(b.Rot);
                    Mix(b.Turn);
                }
            return h;
        }
    }

    private void Build(Chunk chunk)
    {
        foreach (var (_, mesh) in chunk.Meshes) Raylib.UnloadMesh(mesh);
        chunk.Meshes.Clear();
        chunk.Waiting = false;
        var builders = new Dictionary<int, MeshBuilder>();
        MeshBuilder For(Region r)
        {
            if (!builders.TryGetValue(r.Page, out var b)) builders[r.Page] = b = new MeshBuilder();
            return b;
        }
        Pic? Picture(int index)
        {
            if (index < 0) return null;
            Pic? pic = pictures.Now(index);
            if (pic == null) chunk.Waiting = true;
            return pic;
        }
        int x0 = chunk.Cx * ChunkSize, z0 = chunk.Cz * ChunkSize;
        for (int z = z0; z < Math.Min(z0 + ChunkSize, grid.H); z++)
            for (int x = x0; x < Math.Min(x0 + ChunkSize, grid.W); x++)
                Cell(x, z, For, Picture);
        if (byChunk.TryGetValue(chunk.Cz * 10000 + chunk.Cx, out var list))
            foreach (Building b in list)
                Box(b, For, Picture);
        foreach (var (page, builder) in builders)
            foreach (Mesh mesh in builder.Finish())
                chunk.Meshes.Add((page, mesh));
    }

    /// <summary>RimWorld point (x, z) at height y in the world: x east, y up, and z north
    /// along -Z, so that with Y up east is on the right of someone facing north.</summary>
    public static Vector3 P(float x, float y, float z) => new(x, y, -z);

    /// <summary>Encodes how a face is lit, for the world shader.</summary>
    private Color Lit(int x, int z, float shade)
    {
        if (!grid.Inside(x, z)) return new Color(0, 255, (int)(shade * 255), 255);
        return new Color((int)(grid.Lamp(x, z) * 255), (int)(grid.Outdoors(x, z) * 255), (int)(shade * 255), 255);
    }

    /// <summary>How the solid part of cell (x, z) stands: from bottom to top, none when top is 0.</summary>
    private (float bottom, float top) Column(int x, int z)
    {
        if (!grid.Inside(x, z)) return (0f, Dims.RockHeight);
        int i = grid.Index(x, z);
        return grid.Codes[i] switch
        {
            Code.Rock => (0f, grid.RockTop[i] > 0 ? grid.RockTop[i] : Dims.RockHeight),
            Code.Wall => (0f, Dims.WallHeight),
            Code.Door => (Dims.DoorHeight, Dims.WallHeight),
            Code.Machine when grid.BuiltTop[i] <= 0f => (0f, Dims.MachineHeight),
            _ => (0f, 0f),
        };
    }

    private void Cell(int x, int z, Func<Region, MeshBuilder> For, Func<int, Pic?> Picture)
    {
        int i = grid.Index(x, z);
        byte code = grid.Codes[i];
        var (bottom, top) = Column(x, z);
        bool solidFloor = top > 0f && bottom <= 0f;
        Color lit = Lit(x, z, 1f);

        // The ground.
        if (!solidFloor)
        {
            float y = code switch { Code.Shallow => -0.04f, Code.Deep => -0.12f, _ => 0f };
            Pic? floor = code is Code.Shallow or Code.Deep ? null : Picture(grid.FloorSkin[i]);
            Pic ground = floor ?? code switch
            {
                Code.Shallow => art.Water,
                Code.Deep => art.Deep,
                Code.Sand => art.Sand,
                Code.Stone => art.Stone,
                Code.Grass => art.GrassGround,
                Code.Floor => art.Floor,
                _ => art.Soil,
            };
            int span = ground.Span;
            float cx = ((x % span) + span) % span, cz = ((z % span) + span) % span;
            float u0 = cx / span, u1 = (cx + 1) / span, v0 = 1f - (cz + 1) / span, v1 = 1f - cz / span;
            For(ground.R).Quad(P(x, y, z), P(x + 1, y, z), P(x + 1, y, z + 1), P(x, y, z + 1),
                ground.R, u0, v0, u1, v1, lit);
        }

        // The ceiling: a built roof's boards, or a cave's rock.
        byte roof = grid.Roofs[i];
        if (roof != (byte)'.' && !(solidFloor && top >= Dims.WallHeight))
        {
            Pic ceiling = roof == (byte)'c' ? art.Roof : art.Cave;
            float y = Dims.WallHeight;
            For(ceiling.R).Quad(P(x, y, z + 1), P(x + 1, y, z + 1), P(x + 1, y, z), P(x, y, z),
                ceiling.R, 0, 0, 1, 1, Lit(x, z, 0.8f));
        }

        // The world's edge: a cliff, so the map does not just stop.
        if (!solidFloor)
        {
            if (x == 0) Side(x, z, -1, 0, 0, Dims.RockHeight, art.Rock, lit, For);
            if (z == 0) Side(x, z, 0, -1, 0, Dims.RockHeight, art.Rock, lit, For);
            if (x == grid.W - 1) Side(x, z, 1, 0, 0, Dims.RockHeight, art.Rock, lit, For);
            if (z == grid.H - 1) Side(x, z, 0, 1, 0, Dims.RockHeight, art.Rock, lit, For);
        }

        if (top <= 0f) return;

        // A solid column: its sides where they show, and its top.
        Pic face = WallFace(x, z, code, Picture);
        foreach (var (dx, dz) in Sides)
        {
            int nx = x + dx, nz = z + dz;
            if (!grid.Inside(nx, nz)) continue;
            var (nb, nt) = Column(nx, nz);
            Color light = Lit(nx, nz, dx != 0 ? 0.84f : 1f);
            // What of [bottom, top] the neighbour's own column does not cover.
            if (nt <= 0f || nt <= bottom || nb >= top)
                Side(x, z, dx, dz, bottom, top, face, light, For, inward: false);
            else
            {
                if (bottom < nb) Side(x, z, dx, dz, bottom, nb, face, light, For, inward: false);
                if (top > nt) Side(x, z, dx, dz, nt, top, face, light, For, inward: false);
            }
        }
        // A door's lintel shows its underside over the doorway.
        if (code == Code.Door)
            For(face.R).Quad(P(x, bottom, z), P(x + 1, bottom, z), P(x + 1, bottom, z + 1), P(x, bottom, z + 1),
                face.R, 0, 0, 1, 1, Lit(x, z, 0.7f));
        Pic topPic = code switch
        {
            Code.Rock => art.RockTop,
            Code.Machine => art.Metal,
            _ => Picture(grid.WallSkin[i]) ?? art.Wall,
        };
        For(topPic.R).Quad(P(x, top, z), P(x + 1, top, z), P(x + 1, top, z + 1), P(x, top, z + 1),
            topPic.R, 0, 0, 1, 1, Lit(x, z, 1f));
    }

    private static readonly (int, int)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>What a column's sides wear: a built wall its own face, rock RimWorld has no
    /// art for this client's rock, a lintel the wall beside it.</summary>
    private Pic WallFace(int x, int z, byte code, Func<int, Pic?> Picture)
    {
        switch (code)
        {
            case Code.Rock: return art.Rock;
            case Code.Machine: return art.Metal;
            case Code.Door:
                foreach (var (dx, dz) in Sides)
                    if (grid.CodeAt(x + dx, z + dz) == Code.Wall && Picture(grid.WallSkin[grid.Index(x + dx, z + dz)]) is Pic beside)
                        return beside.Face ?? beside;
                return art.Wall;
            default:
                Pic? wall = Picture(grid.WallSkin[grid.Index(x, z)]);
                return wall?.Face ?? wall ?? art.Wall;
        }
    }

    /// <summary>The side of cell (x, z) facing (dx, dz), from y0 up to y1, in one cell tall
    /// pieces lined up with whole heights so neighbouring walls' textures meet. With
    /// <paramref name="inward"/> it faces back into the cell (the map's edge).</summary>
    private void Side(int x, int z, int dx, int dz, float y0, float y1, Pic pic, Color light, Func<Region, MeshBuilder> For, bool inward = true)
    {
        // The face's left and right ends, as seen from the side it faces.
        Vector3 a, b;
        if (dx == 1) (a, b) = (P(x + 1, 0, z), P(x + 1, 0, z + 1));
        else if (dx == -1) (a, b) = (P(x, 0, z + 1), P(x, 0, z));
        else if (dz == 1) (a, b) = (P(x + 1, 0, z + 1), P(x, 0, z + 1));
        else (a, b) = (P(x, 0, z), P(x + 1, 0, z));
        if (inward) (a, b) = (b, a);
        MeshBuilder builder = For(pic.R);
        float y = y0;
        while (y < y1 - 0.001f)
        {
            float step = MathF.Floor(y) + 1f;
            float next = Math.Min(step, y1);
            // Picture's top at the top of each whole cell of height.
            float v0 = step - next, v1 = step - y;
            builder.Quad(a with { Y = y }, b with { Y = y }, b with { Y = next }, a with { Y = next }, pic.R, 0, v0, 1, v1, light);
            y = next;
        }
    }

    /// <summary>A building as a box: RimWorld's picture of it on top, as RimWorld lays it over
    /// the thing, folding down over its sides.</summary>
    private void Box(Building b, Func<Region, MeshBuilder> For, Func<int, Pic?> Picture)
    {
        float h = Models.Height(b);
        Pic pic = Picture(b.Tex) ?? art.Metal;
        bool tiny = h < 0.06f;
        float inset = tiny ? 0.02f : 0.05f;
        float x0 = b.Rect[0] + inset, z0 = b.Rect[1] + inset, x1 = b.Rect[2] + 1 - inset, z1 = b.Rect[3] + 1 - inset;
        if (Models.Has(b.Def, "Fence") || Models.Has(b.Def, "Column"))
        {
            // Thin posts, not a full cell.
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, half = Models.Has(b.Def, "Column") ? 0.3f : 0.12f;
            x0 = cx - half;
            x1 = cx + half;
            z0 = cz - half;
            z1 = cz + half;
        }
        int lx = Math.Clamp((int)((x0 + x1) / 2), 0, grid.W - 1), lz = Math.Clamp((int)((z0 + z1) / 2), 0, grid.H - 1);
        // The top: each corner's spot on the picture, as RimWorld draws it.
        Vector2 Uv(float px, float pz)
        {
            float dw = Math.Max(0.1f, b.Draw[0]), dh = Math.Max(0.1f, b.Draw[1]);
            float ox = px - b.At[0], oz = pz - b.At[1];
            for (int t = 0; t < ((b.Turn % 4) + 4) % 4; t++) (ox, oz) = (-oz, ox);
            float u = Math.Clamp(0.5f + ox / dw, 0f, 1f), v = Math.Clamp(0.5f - oz / dh, 0f, 1f);
            return pic.R.At(u, v);
        }
        MeshBuilder builder = For(pic.R);
        Vector3 sw = new(x0, h, z0), se = new(x1, h, z0), ne = new(x1, h, z1), nw = new(x0, h, z1);
        Vector2 tsw = Uv(x0, z0), tse = Uv(x1, z0), tne = Uv(x1, z1), tnw = Uv(x0, z1);
        builder.Quad(sw, se, ne, nw, tsw, tse, tne, tnw, Lit(lx, lz, 1f));
        if (tiny) return;
        // The sides: a band of the picture along that edge, folded down.
        void Fold(Vector3 a, Vector3 c, Vector2 ta, Vector2 tc, Vector2 inA, Vector2 inC, float shade)
        {
            builder.Quad(a with { Y = 0 }, c with { Y = 0 }, c, a, inA, inC, tc, ta, Lit(lx, lz, shade));
        }
        Vector2 In(Vector2 edge, Vector2 across) => edge + (across - edge) * 0.18f;
        Fold(sw, se, tsw, tse, In(tsw, tnw), In(tse, tne), 0.78f); // south
        Fold(se, ne, tse, tne, In(tse, tsw), In(tne, tnw), 0.66f); // east
        Fold(ne, nw, tne, tnw, In(tne, tse), In(tnw, tsw), 0.78f); // north
        Fold(nw, sw, tnw, tsw, In(tnw, tne), In(tsw, tse), 0.66f); // west
    }

    /// <summary>Draws every chunk near enough to see, with <paramref name="material"/> for
    /// each page: the eye at RimWorld (x, z), looking along RimWorld (dx, dz) and up dy.</summary>
    public void Draw(Func<int, Material> material, Vector2 eye, Vector2 look, float up, float reach)
    {
        Vector2 flat = look.LengthSquared() > 1e-6f ? Vector2.Normalize(look) : new Vector2(0, 1);
        float far = reach + ChunkSize;
        foreach (Chunk chunk in chunks)
        {
            if (chunk.Meshes.Count == 0) continue;
            var centre = new Vector2((chunk.Cx + 0.5f) * ChunkSize, (chunk.Cz + 0.5f) * ChunkSize);
            Vector2 to = centre - eye;
            float distance = to.Length();
            if (distance > far) continue;
            // Behind the eye (a chunk is up to 12 cells from its middle to a corner).
            if (Vector2.Dot(to, flat) < -ChunkSize * 0.75f && Math.Abs(up) < 0.9f) continue;
            foreach (var (page, mesh) in chunk.Meshes)
                Raylib.DrawMesh(mesh, material(page), Matrix4x4.Identity);
        }
    }

    private void Clear()
    {
        foreach (Chunk chunk in chunks)
        {
            foreach (var (_, mesh) in chunk.Meshes) Raylib.UnloadMesh(mesh);
            chunk.Meshes.Clear();
        }
        chunks = Array.Empty<Chunk>();
    }

    public void Dispose() => Clear();
}
