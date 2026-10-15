using System.Numerics;
using Raylib_cs;

namespace RimFps.World;

/// <summary>
/// Collects textured, coloured quads for one atlas page and uploads them as raylib meshes,
/// starting another mesh before 16-bit indices run out.
/// </summary>
public sealed class MeshBuilder
{
    private const int MostVertices = 65000;

    private readonly List<Vector3> positions = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Color> colors = new();
    private readonly List<Mesh> done = new();

    public bool Empty => positions.Count == 0 && done.Count == 0;

    /// <summary>A quad a, b, c, d (going round it) with texture coordinates ta..td.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td, Color color)
    {
        if (positions.Count + 4 > MostVertices) Flush();
        positions.Add(a);
        positions.Add(b);
        positions.Add(c);
        positions.Add(d);
        uvs.Add(ta);
        uvs.Add(tb);
        uvs.Add(tc);
        uvs.Add(td);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
    }

    /// <summary>A quad showing the part (u0, v0)..(u1, v1) of region r, a at its bottom left
    /// going round: bottom left, bottom right, top right, top left.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Region r, float u0, float v0, float u1, float v1, Color color) =>
        Quad(a, b, c, d, r.At(u0, v1), r.At(u1, v1), r.At(u1, v0), r.At(u0, v0), color);

    private unsafe void Flush()
    {
        if (positions.Count == 0) return;
        int quads = positions.Count / 4;
        var mesh = new Mesh(positions.Count, quads * 2);
        mesh.AllocVertices();
        mesh.AllocTexCoords();
        mesh.AllocColors();
        mesh.AllocIndices();
        Span<Vector3> v = mesh.VerticesAs<Vector3>();
        Span<Vector2> t = mesh.TexCoordsAs<Vector2>();
        Span<Color> c = mesh.ColorsAs<Color>();
        Span<ushort> idx = mesh.IndicesAs<ushort>();
        for (int i = 0; i < positions.Count; i++)
        {
            v[i] = positions[i];
            t[i] = uvs[i];
            c[i] = colors[i];
        }
        for (int q = 0; q < quads; q++)
        {
            int b = q * 4, k = q * 6;
            idx[k] = (ushort)b;
            idx[k + 1] = (ushort)(b + 1);
            idx[k + 2] = (ushort)(b + 2);
            idx[k + 3] = (ushort)b;
            idx[k + 4] = (ushort)(b + 2);
            idx[k + 5] = (ushort)(b + 3);
        }
        Raylib.UploadMesh(ref mesh, false);
        done.Add(mesh);
        positions.Clear();
        uvs.Clear();
        colors.Clear();
    }

    /// <summary>The meshes made, uploaded; the builder is empty after.</summary>
    public List<Mesh> Finish()
    {
        Flush();
        var meshes = new List<Mesh>(done);
        done.Clear();
        return meshes;
    }
}
