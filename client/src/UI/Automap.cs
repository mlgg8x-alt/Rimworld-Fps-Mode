using System.Numerics;
using RimFps.Game;
using RimFps.World;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// M: Doom's automap. The map's walls as lines over black, the way you face pointing up, doors
/// in yellow, rock in brown, people as arrows: green yours, red hostile.
/// </summary>
public sealed class Automap
{
    public bool Open;
    public float Zoom = 5f;

    public void Draw(int w, int h, Grid grid, Player player, Figures figures)
    {
        if (!Open || !grid.Ready) return;
        if (Raylib.IsKeyPressed(KeyboardKey.Equal) || Raylib.IsKeyPressed(KeyboardKey.KpAdd)) Zoom = Math.Min(Zoom + 1, 12);
        if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract)) Zoom = Math.Max(Zoom - 1, 2);
        int floor = h;
        float zoom = Zoom * Ui.Scale * 1.6f;
        Raylib.DrawRectangle(0, 0, w, floor, new Color(0, 0, 0, 225));
        float yaw = player.Yaw * MathF.PI / 180f;
        float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
        var centre = new Vector2(w / 2f, floor / 2f);
        Vector2 Screen(float x, float z)
        {
            float dx = x - player.X, dz = z - player.Z;
            // Turned so the way the player faces is up the screen.
            float rx = dx * cos - dz * sin, rz = dx * sin + dz * cos;
            return centre + new Vector2(rx, -rz) * zoom;
        }
        int reach = (int)(Math.Max(w, floor) / zoom / 1.4f) + 2;
        int px = (int)player.X, pz = (int)player.Z;
        for (int z = Math.Max(0, pz - reach); z < Math.Min(grid.H, pz + reach); z++)
            for (int x = Math.Max(0, px - reach); x < Math.Min(grid.W, px + reach); x++)
            {
                byte code = grid.Codes[grid.Index(x, z)];
                bool solid = code is Code.Wall or Code.Rock or Code.Machine;
                if (!solid && code != Code.Door) continue;
                Color c = code switch
                {
                    Code.Door => new Color(230, 210, 60, 255),
                    Code.Rock => new Color(140, 96, 60, 255),
                    Code.Machine => new Color(120, 120, 140, 255),
                    _ => new Color(220, 50, 40, 255),
                };
                // An edge wherever this cell meets one that is not like it.
                bool Open(int nx, int nz) => grid.Inside(nx, nz) && grid.Codes[grid.Index(nx, nz)] is not (Code.Wall or Code.Rock or Code.Machine);
                if (code == Code.Door || Open(x - 1, z)) Line(Screen(x, z), Screen(x, z + 1), c);
                if (code == Code.Door || Open(x + 1, z)) Line(Screen(x + 1, z), Screen(x + 1, z + 1), c);
                if (code == Code.Door || Open(x, z - 1)) Line(Screen(x, z), Screen(x + 1, z), c);
                if (code == Code.Door || Open(x, z + 1)) Line(Screen(x, z + 1), Screen(x + 1, z + 1), c);
            }
        foreach (Figure f in figures.All.Values)
        {
            if (f.Id == player.Playing || f.Pawn.Dead) continue;
            Color c = f.Pawn.Hostile ? new Color(255, 60, 40, 255) : f.Pawn.Faction == "player" ? new Color(80, 230, 80, 255) : new Color(200, 200, 200, 255);
            Arrow(Screen(f.X, f.Z), (f.Yaw - player.Yaw) * MathF.PI / 180f, c, f.Pawn.Downed, Ui.Scale * 1.5f);
        }
        Arrow(centre, 0, Color.White, false, Ui.Scale * 1.5f);
        Ui.Text("Map  (+/- to zoom, M to close)", Ui.S(12), h - Ui.S(34), Ui.S(18), Ui.Grey);
    }

    private static void Line(Vector2 a, Vector2 b, Color c) => Raylib.DrawLineEx(a, b, Math.Max(1f, Ui.Scale * 1.5f), c);

    private static void Arrow(Vector2 at, float turn, Color c, bool downed, float k)
    {
        if (downed)
        {
            Raylib.DrawCircleV(at, 2 * k, c);
            return;
        }
        var tip = new Vector2(MathF.Sin(turn), -MathF.Cos(turn));
        var side = new Vector2(-tip.Y, tip.X);
        Raylib.DrawLineEx(at - tip * 4 * k, at + tip * 5 * k, k, c);
        Raylib.DrawLineEx(at + tip * 5 * k, at + tip * k + side * 3 * k, k, c);
        Raylib.DrawLineEx(at + tip * 5 * k, at + tip * k - side * 3 * k, k, c);
    }
}
