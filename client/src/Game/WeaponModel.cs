using System.Numerics;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// A weapon to hold, made from nothing but its RimWorld picture, so every weapon works, modded
/// ones too: the picture is the weapon seen from the side, kept flat and 2D, and turned in the
/// view so perspective makes it read as solid; a thin stack of darker copies behind it gives
/// it an edge. Where the hands go is read off its silhouette (the grip hangs below the body in
/// the rear half, the barrel's underside takes the other hand, the muzzle is the far end), and
/// the player can move all of it in the HUD's edit mode.
///
/// Model space: x runs from the weapon's back (0) to its tip (<see cref="Length"/>), as RimWorld
/// draws weapons pointing east; y is up; z is across, the thickness. Units are cells.
/// </summary>
public sealed class WeaponModel : IDisposable
{
    public readonly List<Mesh> Meshes = new();
    /// <summary>Copies of the picture that make up its thickness.</summary>
    private const int Layers = 28;
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
        // The weapon stays RimWorld's flat picture, turned in the view so it reads as solid:
        // the picture on both sides and a stack of darker copies between them, cut out the same
        // way, which makes a clean edge of the right shape and colour without any blockiness.
        for (int i = 0; i <= Layers; i++)
        {
            float z = -t + 2 * t * i / Layers;
            bool side = i == 0 || i == Layers;
            byte shade = (byte)(side ? (i == Layers ? 255 : 228) : 150 + 40 * Math.Abs(i - Layers / 2f) / (Layers / 2f));
            var tint = new Color(shade, shade, shade, (byte)255);
            builder.Quad(new(0, 0, z), new(L, 0, z), new(L, H, z), new(0, H, z), Uv(0, h), Uv(w, h), Uv(w, 0), Uv(0, 0), tint);
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
