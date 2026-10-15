using System.Numerics;
using RimFps.Net;
using RimFps.Render;
using RimFps.World;
using Raylib_cs;

namespace RimFps.Game;

/// <summary>
/// What stands about the map as sprites, as a 90s shooter's decorations: RimWorld's trees,
/// bushes, crops and grass as its own pictures, sized by how grown they are; things lying on
/// the ground (stacked on shelves where there are shelves); the buildings RimWorld sends a
/// standing picture of (lamps, torches, statues: lamps lit, glowing); and the doors, slabs
/// that slide up into their lintel as RimWorld swings them, as Doom's doors do.
/// </summary>
public sealed class Scenery
{
    private const float PlantReach = 75f, GrassReach = 24f, ItemReach = 45f, PlantLeast = 0.35f;

    private static float PlantCells(string kind) => kind switch
    {
        "pine" or "oak" or "birch" or "palm" => 3.4f,
        "dead" => 2.9f,
        "cactus" => 1.2f,
        "grass" => 0.8f,
        _ when kind.StartsWith("crop_") => 0.75f,
        _ => 1.1f,
    };

    private static float Hash(int x, int z, int salt) => PixelArt.Rand(x, z, salt);

    private static int? PlantPicture(Dictionary<string, List<int>> art, string kind, int x, int z)
    {
        if (!art.TryGetValue(kind, out List<int>? list) || list.Count == 0) return null;
        return list[(int)(Hash(x, z, 7) * list.Count) % list.Count];
    }

    public void Draw(Sprites sprites, Link link, Grid grid, Pictures pictures, Builtins art, Vector3 eye, float sky, Func<float, float, float, Color> lit)
    {
        float ex = eye.X, ez = -eye.Z;
        bool Near(float x, float z, float reach) => (x - ex) * (x - ex) + (z - ez) * (z - ez) <= reach * reach;

        // Trees, bushes, crops.
        var soil = new Dictionary<int, float>();
        foreach (Building b in link.Buildings)
        {
            float top = Models.SoilTop(b.Def);
            if (top <= 0) continue;
            for (int z = b.Rect[1]; z <= b.Rect[3]; z++)
                for (int x = b.Rect[0]; x <= b.Rect[2]; x++)
                    if (grid.Inside(x, z)) soil[grid.Index(x, z)] = top;
        }
        foreach (Plant plant in link.Plants)
        {
            if (!Near(plant.X + 0.5f, plant.Z + 0.5f, PlantReach)) continue;
            if (PlantPicture(link.PlantArt, plant.Kind, plant.X, plant.Z) is not int index || pictures.Get(index) is not Pic pic) continue;
            float grown = PlantLeast + (1 - PlantLeast) * Math.Clamp(plant.Growth, 0, 1);
            float cells = PlantCells(plant.Kind) * grown;
            float longest = Math.Max(pic.CutW, pic.CutH);
            float k = cells / Math.Max(longest, 1e-3f);
            float lift = grid.Inside(plant.X, plant.Z) && soil.TryGetValue(grid.Index(plant.X, plant.Z), out float s) ? s : 0f;
            float x = plant.X + 0.5f, z = plant.Z + 0.5f;
            sprites.Stand(pic, WorldMesh.P(x, lift, z), pic.CutW * k, pic.CutH * k, lit(x, z, 1f), flip: Hash(plant.X, plant.Z, 3) > 0.5f);
        }

        // Grass on the grassy cells nearby, on a grid fixed to the map.
        if (PlantPicture(link.PlantArt, "grass", 0, 0) != null && grid.Ready)
        {
            int cx = (int)ex, cz = (int)ez, r = (int)GrassReach;
            for (int z = Math.Max(0, cz - r); z <= Math.Min(grid.H - 1, cz + r); z++)
                for (int x = Math.Max(0, cx - r); x <= Math.Min(grid.W - 1, cx + r); x++)
                {
                    if (grid.Codes[grid.Index(x, z)] != Code.Grass) continue;
                    if (Hash(x, z, 17) < 0.55f) continue;
                    float jx = x + Hash(x, z, 11), jz = z + Hash(x, z, 13);
                    if (!Near(jx, jz, GrassReach)) continue;
                    if (PlantPicture(link.PlantArt, "grass", x, z) is not int index || pictures.Get(index) is not Pic pic) continue;
                    float k = 0.4f / Math.Max(Math.Max(pic.CutW, pic.CutH), 1e-3f);
                    sprites.Stand(pic, WorldMesh.P(jx, 0, jz), pic.CutW * k, pic.CutH * k, lit(jx, jz, 0.2f), flip: Hash(x, z, 19) > 0.5f);
                }
        }

        // What lies on the ground.
        var stacks = new Dictionary<int, int>();
        foreach (Item item in link.Items)
        {
            float x = item.X + 0.5f, z = item.Z + 0.5f;
            if (!Near(x, z, ItemReach) || pictures.Get(item.Tex) is not Pic pic) continue;
            float lift = 0, most = 0.42f;
            bool chunk = item.Def.StartsWith("Chunk");
            if (grid.Inside(item.X, item.Z))
            {
                int i = grid.Index(item.X, item.Z);
                if (grid.Shelf[i])
                {
                    stacks.TryGetValue(i, out int slot);
                    stacks[i] = slot + 1;
                    lift = new[] { 0.05f, 0.55f, 1.05f }[slot % 3];
                    most = 0.35f;
                }
                else lift = grid.BuiltTop[i] > 0.05f ? grid.BuiltTop[i] : 0;
            }
            if (chunk) most = 0.75f;
            float k = most / Math.Max(Math.Max(pic.CutW, pic.CutH), 1e-3f);
            sprites.Stand(pic, WorldMesh.P(x, lift, z), pic.CutW * k, pic.CutH * k, lit(x, z, lift + 0.2f));
        }

        // Buildings RimWorld stands up as pictures.
        foreach (Building b in link.Buildings)
        {
            if (!Models.Standing(b) || !Near(b.At[0], b.At[1], PlantReach)) continue;
            if (pictures.Get(b.Card) is not Pic pic) continue;
            float tall = Models.StandingHeight(b);
            float k = tall / Math.Max(pic.CutH, 1e-3f);
            float x = b.At[0], z = b.At[1], lift = 0;
            if (Models.OnWall(b))
            {
                // Hung on the wall behind it, at head height.
                (float dx, float dz) = (b.Rot % 4) switch { 0 => (0f, 1f), 1 => (1f, 0f), 2 => (0f, -1f), _ => (-1f, 0f) };
                x -= dx * 0.4f;
                z -= dz * 0.4f;
                lift = 1.15f;
            }
            bool glows = Models.Has(b.Def, "Lamp") || Models.Has(b.Def, "Torch") || Models.Has(b.Def, "Brazier")
                || Models.Has(b.Def, "Campfire") || Models.Has(b.Def, "Sconce") || Models.Has(b.Def, "Lightball");
            sprites.Stand(pic, WorldMesh.P(x, lift, z), pic.CutW * k, tall, glows ? Color.White : lit(x, z, tall * 0.5f), b.Flip, bright: glows);
        }

        // Doors: a slab in the middle of the doorway, slid up into the lintel as far as
        // RimWorld has swung the door open.
        foreach (var (index, door) in grid.DoorAt)
        {
            int x = door.X, z = door.Z;
            if (!Near(x + 0.5f, z + 0.5f, PlantReach)) continue;
            float open = grid.DoorSwing(x, z);
            if (open >= 0.98f) continue;
            float bottom = open * Dims.DoorHeight, top = Dims.DoorHeight;
            Color tint = Color.White;
            if (pictures.Get(grid.WallSkin[index]) is Pic doorPic)
                tint = PixelArt.Mix(Color.White, doorPic.Average, 0.65f);
            Color l1 = lit(x + 0.5f, z + 0.5f, 0.8f);
            Color color = new((byte)(tint.R * l1.R / 255), (byte)(tint.G * l1.G / 255), (byte)(tint.B * l1.B / 255), (byte)255);
            // Axis 0: the leaves slide east and west, so the slab runs east-west.
            bool eastWest = door.Axis == 0;
            const float half = 0.07f;
            Vector3 a, b, n;
            if (eastWest)
            {
                a = WorldMesh.P(x, 0, z + 0.5f);
                b = WorldMesh.P(x + 1, 0, z + 0.5f);
                n = new Vector3(0, 0, half);
            }
            else
            {
                a = WorldMesh.P(x + 0.5f, 0, z);
                b = WorldMesh.P(x + 0.5f, 0, z + 1);
                n = new Vector3(half, 0, 0);
            }
            Vector3 up0 = new(0, bottom, 0), up1 = new(0, top, 0);
            // The slab's lower part shows; its top has slid up into the lintel.
            float v0 = open, v1 = 1;
            foreach (Vector3 side in new[] { n, -n })
                sprites.Add(art.Door.R, a + side + up0, b + side + up0, b + side + up1, a + side + up1, 0, v0, 1, v1, color);
            // Its bottom edge.
            sprites.Add(art.Door.R, a + n + up0, b + n + up0, b - n + up0, a - n + up0, 0, 0.95f, 1, 1, PixelArt.Mix(color, Color.Black, 0.4f));
        }
    }
}
