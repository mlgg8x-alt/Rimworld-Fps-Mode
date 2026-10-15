using System.Numerics;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// A weapon in 3D, made from nothing but its RimWorld picture, so every weapon works, modded
/// ones too: the picture is the weapon seen from the side, so it becomes the weapon's two flat
/// sides, and every pixel along its outline grows a wall the colour of that pixel, giving the
/// weapon its thickness (the way a voxel game turns a sprite into a held item). Where the hands
/// go is read off its silhouette: the grip hangs below the body in the rear half, the barrel's
/// underside takes the other hand, and the muzzle is the far end.
///
/// Model space: x runs from the weapon's back (0) to its tip (<see cref="Length"/>), as RimWorld
/// draws weapons pointing east; y is up; z is across, the thickness. Units are cells.
/// </summary>
public sealed class WeaponModel : IDisposable
{
    public readonly List<Mesh> Meshes = new();
    public int Page;
    public float Length, Height, Thickness;
    public bool Melee;
    public Vector2 Grip, Fore, Muzzle, Sight;
    /// <summary>Two hands: the weapon is long enough to hold by its barrel too.</summary>
    public bool TwoHanded;

    private const int Detail = 64;

    /// <summary>Builds the model of picture <paramref name="index"/>, drawn <paramref name="size"/>
    /// cells across in RimWorld; null while the picture is not loaded.</summary>
    public static WeaponModel? Build(Pictures pictures, int index, Vector2 size, bool melee)
    {
        Pic? pic = pictures.Get(index);
        if (pic == null) return null;
        var read = pictures.Pixels(index, Detail);
        if (read == null) return null;
        var (pixels, w, h) = read.Value;
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && pixels[y * w + x].A >= 128;

        var model = new WeaponModel
        {
            Page = pic.R.Page,
            Melee = melee,
            Length = Math.Max(0.05f, size.X * pic.CutW),
            Height = Math.Max(0.02f, size.Y * pic.CutH),
        };
        model.Thickness = Math.Clamp(model.Length * (melee ? 0.035f : 0.055f), 0.02f, 0.09f);
        float L = model.Length, H = model.Height, t = model.Thickness / 2f;
        // Picture pixel (x, y) (y down) to model space.
        Vector2 At(float x, float y) => new(x / w * L, (1f - y / h) * H);
        // The solid part of the picture in the atlas.
        float cu0 = pic.Cut[0], cv0 = pic.Cut[1], cw = pic.CutW, ch = pic.CutH;
        Vector2 Uv(float x, float y) => pic.R.At(cu0 + cw * Math.Clamp(x / w, 0f, 1f), cv0 + ch * Math.Clamp(y / h, 0f, 1f));

        var builder = new MeshBuilder();
        var face = new Color(255, 255, 255, 255);
        var back = new Color(205, 205, 205, 255);
        // The two flat sides: the picture, cut out by the shader.
        builder.Quad(new(0, 0, t), new(L, 0, t), new(L, H, t), new(0, H, t), Uv(0, h), Uv(w, h), Uv(w, 0), Uv(0, 0), face);
        builder.Quad(new(L, 0, -t), new(0, 0, -t), new(0, H, -t), new(L, H, -t), Uv(w, h), Uv(0, h), Uv(0, 0), Uv(w, 0), back);
        // The walls round its outline, each the colour of the pixel it stands on (vertex alpha 0
        // tells the shader not to cut them).
        var top = new Color(255, 255, 255, 0);
        var bottom = new Color(130, 130, 130, 0);
        var end = new Color(185, 185, 185, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Solid(x, y)) continue;
                Vector2 uv = Uv(x + 0.5f, y + 0.5f);
                Vector2 a = At(x, y), b = At(x + 1, y + 1); // a top left, b bottom right
                if (!Solid(x, y - 1))
                    builder.Quad(new(a.X, a.Y, t), new(b.X, a.Y, t), new(b.X, a.Y, -t), new(a.X, a.Y, -t), uv, uv, uv, uv, top);
                if (!Solid(x, y + 1))
                    builder.Quad(new(a.X, b.Y, -t), new(b.X, b.Y, -t), new(b.X, b.Y, t), new(a.X, b.Y, t), uv, uv, uv, uv, bottom);
                if (!Solid(x - 1, y))
                    builder.Quad(new(a.X, b.Y, -t), new(a.X, b.Y, t), new(a.X, a.Y, t), new(a.X, a.Y, -t), uv, uv, uv, uv, end);
                if (!Solid(x + 1, y))
                    builder.Quad(new(b.X, b.Y, t), new(b.X, b.Y, -t), new(b.X, a.Y, -t), new(b.X, a.Y, t), uv, uv, uv, uv, end);
            }
        model.Meshes.AddRange(builder.Finish());
        model.FindHands(Solid, w, h, At);
        return model;
    }

    /// <summary>Where the hands and the muzzle are, from the silhouette.</summary>
    private void FindHands(Func<int, int, bool> solid, int w, int h, Func<float, float, Vector2> at)
    {
        var topRow = new int[w];
        var bottomRow = new int[w];
        for (int x = 0; x < w; x++)
        {
            topRow[x] = -1;
            bottomRow[x] = -1;
            for (int y = 0; y < h; y++)
                if (solid(x, y))
                {
                    if (topRow[x] < 0) topRow[x] = y;
                    bottomRow[x] = y;
                }
        }
        float Middle(int x) => topRow[x] < 0 ? h / 2f : (topRow[x] + bottomRow[x]) / 2f;
        int last = Array.FindLastIndex(bottomRow, b => b >= 0);
        if (last < 0) last = w - 1;
        if (Melee)
        {
            // A blade's handle is its back end: hold it a little way in.
            int hx = Math.Clamp((int)(w * 0.1f), 0, w - 1);
            Grip = at(hx + 0.5f, Middle(hx));
            Fore = at(Math.Min(hx + 3, w - 1) + 0.5f, Middle(Math.Min(hx + 3, w - 1)));
            Muzzle = at(last + 1, Middle(last));
            Sight = Grip;
            TwoHanded = false;
            return;
        }
        // The body's underside, as most columns have it; the grip hangs below it in the rear half.
        var under = Enumerable.Range((int)(w * 0.2f), Math.Max(1, (int)(w * 0.7f)))
            .Where(x => x < w && bottomRow[x] >= 0).Select(x => bottomRow[x]).OrderBy(v => v).ToList();
        float body = under.Count > 0 ? under[under.Count / 2] : h * 0.6f;
        int gripX = (int)(w * 0.35f);
        float deepest = body + 1.5f;
        for (int x = (int)(w * 0.12f); x < (int)(w * 0.62f); x++)
            if (bottomRow[x] > deepest)
            {
                deepest = bottomRow[x];
                gripX = x;
            }
        float gripY = deepest > body + 1.5f ? (body + deepest) / 2f + 1 : body;
        Grip = at(gripX + 0.5f, gripY);
        int foreX = Math.Clamp(Math.Max(gripX + (int)(w * 0.25f), (int)(w * 0.6f)), 0, Math.Max(0, last - 2));
        Fore = at(foreX + 0.5f, bottomRow[foreX] >= 0 ? bottomRow[foreX] : body);
        Muzzle = at(last + 1, Middle(last));
        int sightX = Math.Clamp(gripX, 0, w - 1);
        Sight = at(sightX + 0.5f, topRow[sightX] >= 0 ? topRow[sightX] : 0);
        TwoHanded = Length > 0.7f;
    }

    public void Draw(Material material, Matrix4x4 transform)
    {
        foreach (Mesh mesh in Meshes) Raylib.DrawMesh(mesh, material, transform);
    }

    public void Dispose()
    {
        foreach (Mesh mesh in Meshes) Raylib.UnloadMesh(mesh);
        Meshes.Clear();
    }
}
