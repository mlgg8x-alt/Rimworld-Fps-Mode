using System.Numerics;
using RimFps.Game;
using RimFps.Net;
using RimFps.World;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>What the HUD needs to know this frame.</summary>
public sealed class HudFrame
{
    public int W, H;
    public Link Link = null!;
    public Gun Gun = null!;
    public Player Player = null!;
    public Atlas Atlas = null!;
    public Pictures Pictures = null!;
    public Figures Figures = null!;
    public Grid Grid = null!;
    public float Aim;
    public string? LookingAt, Prompt;
    public bool Overlay;
    public Vector2 Mouse;
}

/// <summary>
/// The HUD: a few floating widgets and nothing else. RimWorld's own live view around you as the
/// minimap (RimWorld's window, placed by the mod where this widget is); a pawn preview of the
/// colonist, their RimWorld portrait with each limb's health around it, Pip-Boy style; the
/// weapon in hand and what is left of its burst; a compass with the raid on it; messages; what E
/// would do. F2 (or Edit HUD in the menu) lets each be dragged, resized with the wheel and hidden
/// with a right click; the layout is saved.
/// </summary>
public sealed class Hud
{
    private static readonly Dictionary<string, Vector2> BaseSize = new()
    {
        ["minimap"] = new(300, 300),
        ["status"] = new(472, 322),
        ["weapon"] = new(340, 116),
        ["compass"] = new(560, 46),
        ["messages"] = new(560, 120),
        ["prompt"] = new(280, 44),
    };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["minimap"] = "MINIMAP (RimWorld's view)",
        ["status"] = "HEALTH (RimWorld's health tab)",
        ["weapon"] = "WEAPON",
        ["compass"] = "COMPASS",
        ["messages"] = "MESSAGES",
        ["prompt"] = "PROMPT",
    };

    private readonly Settings settings;
    private readonly List<(string text, float left, Color color)> messages = new();
    private readonly List<(float bearing, float left)> damage = new();
    private readonly Dictionary<string, Rectangle> rects = new();
    private float hurt, hit, kill, lastHealth = -1, faceFlash, time;
    private bool headHit;
    private string? dragging;
    private Vector2 grab;

    public bool Editing;

    /// <summary>The mouse is on a widget, or dragging one: the weapon editor leaves it alone.</summary>
    public bool Busy(Vector2 mouse) => dragging != null || rects.Values.Any(r => Raylib.CheckCollisionPointRec(mouse, r));
    /// <summary>The layout changed (moved, resized, shown, hidden): the minimap's place goes to RimWorld.</summary>
    public bool LayoutChanged = true;

    public Hud(Settings settings) => this.settings = settings;

    public void Message(string text, Color? color = null, float seconds = 3.5f)
    {
        messages.RemoveAll(m => m.text == text);
        messages.Add((text, seconds, color ?? Ui.White));
        if (messages.Count > 5) messages.RemoveAt(0);
    }

    public void Hurt(float amount = 1f)
    {
        hurt = Math.Clamp(Math.Max(hurt, 0.35f * amount), 0, 0.6f);
        faceFlash = 0.5f;
    }

    public void Hit(string? part)
    {
        hit = 0.16f;
        headHit = part == "head";
    }

    public void Kill(string who)
    {
        kill = 0.7f;
        Message($"Downed {who}", Ui.Red, 3f);
    }

    /// <summary>Struck from (x, z) in RimWorld: a red arc points there for a moment.</summary>
    public void DamageFrom(float x, float z, Player player)
    {
        float bearing = Figures.Heading(x - player.X, z - player.Z);
        damage.Add((bearing, 1.6f));
        if (damage.Count > 6) damage.RemoveAt(0);
    }

    public void Update(float dt, Link link)
    {
        time += dt;
        hurt = Math.Max(0, hurt - dt * 1.2f);
        hit = Math.Max(0, hit - dt);
        kill = Math.Max(0, kill - dt);
        faceFlash = Math.Max(0, faceFlash - dt);
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var m = messages[i];
            m.left -= dt;
            if (m.left <= 0) messages.RemoveAt(i);
            else messages[i] = m;
        }
        for (int i = damage.Count - 1; i >= 0; i--)
        {
            var d = damage[i];
            d.left -= dt;
            if (d.left <= 0) damage.RemoveAt(i);
            else damage[i] = d;
        }
        float health = link.Status?.Health ?? link.Snap?.Health ?? -1;
        if (health >= 0)
        {
            if (lastHealth >= 0 && health < lastHealth - 0.004f) Hurt(Math.Clamp((lastHealth - health) * 10f, 0.5f, 1.6f));
            lastHealth = health;
        }
    }

    /// <summary>Where a widget is on the screen now, in pixels.</summary>
    public Rectangle Rect(string id, int w, int h)
    {
        Widget wd = settings.Hud[id];
        Vector2 size = BaseSize[id] * Ui.Scale * wd.Scale;
        float x = Math.Clamp(wd.X * w, 0, Math.Max(0, w - size.X)), y = Math.Clamp(wd.Y * h, 0, Math.Max(0, h - size.Y));
        return new Rectangle(x, y, size.X, size.Y);
    }

    /// <summary>Where RimWorld's window should sit as the minimap, in our window's pixels.</summary>
    public (Rectangle rect, bool shown, bool round, float zoom) Minimap(int w, int h)
    {
        Widget wd = settings.Hud["minimap"];
        return (Rect("minimap", w, h), wd.Shown && !Editing, wd.Round, wd.Zoom);
    }

    public void Draw(HudFrame f)
    {
        int w = f.W, h = f.H;
        PawnState? pawn = f.Link.Player;
        Vignette(w, h);
        bool playing = pawn != null;
        foreach (string id in BaseSize.Keys)
        {
            Widget wd = settings.Hud[id];
            Rectangle r = Rect(id, w, h);
            rects[id] = r;
            if (!wd.Shown && !Editing) continue;
            if (!playing && id is "status" or "weapon" or "prompt" && !Editing) continue;
            switch (id)
            {
                case "minimap": MinimapWidget(f, r, wd); break;
                case "status": StatusWidget(f, r); break;
                case "weapon": WeaponWidget(f, r); break;
                case "compass": CompassWidget(f, r); break;
                case "messages": MessagesWidget(r); break;
                case "prompt": PromptWidget(f, r); break;
            }
            if (Editing) EditFrame(id, r, wd);
        }
        if (playing && !Editing) Crosshair(f);
        if (f.Link.Paused && !Editing) Ui.Centred("PAUSED IN RIMWORLD", w / 2f, h * 0.3f, Ui.S(30), Ui.Gold);
        if (Editing) EditHelp(w, h);
    }

    // ------------------------------------------------------------------ fixed elements

    private void Vignette(int w, int h)
    {
        if (hurt <= 0.01f) return;
        byte a = (byte)(255 * Math.Clamp(hurt, 0, 0.6f));
        var red = new Color((byte)170, (byte)0, (byte)0, a);
        var none = new Color((byte)170, (byte)0, (byte)0, (byte)0);
        int band = (int)(Math.Min(w, h) * 0.18f);
        Raylib.DrawRectangleGradientV(0, 0, w, band, red, none);
        Raylib.DrawRectangleGradientV(0, h - band, w, band, none, red);
        Raylib.DrawRectangleGradientH(0, 0, band, h, red, none);
        Raylib.DrawRectangleGradientH(w - band, 0, band, h, none, red);
    }

    private void Crosshair(HudFrame f)
    {
        float cx = f.W / 2f, cy = f.H / 2f;
        Gun gun = f.Gun;
        if (gun.Kind == Gun.Mode.Gun && !f.Player.Sprinting)
        {
            if (f.Aim > 0.8f) Raylib.DrawCircleV(new Vector2(cx, cy), Ui.S(2.2f), new Color(255, 255, 255, 220));
            else
            {
                // The gap opens with the spread: where the next bullet may go.
                float gap = Ui.S(6) + gun.Spread * Ui.S(9) * (1f + f.Player.Moving);
                float len = Ui.S(9), th = Math.Max(1f, Ui.S(2));
                var c = new Color(255, 255, 255, (int)(220 * (1 - f.Aim)));
                Raylib.DrawRectangleRec(new Rectangle(cx - gap - len, cy - th / 2, len, th), c);
                Raylib.DrawRectangleRec(new Rectangle(cx + gap, cy - th / 2, len, th), c);
                Raylib.DrawRectangleRec(new Rectangle(cx - th / 2, cy - gap - len, th, len), c);
                Raylib.DrawRectangleRec(new Rectangle(cx - th / 2, cy + gap, th, len), c);
            }
        }
        else if (gun.Kind != Gun.Mode.Gun)
            Raylib.DrawCircleLinesV(new Vector2(cx, cy), Ui.S(5), new Color(255, 255, 255, 160));
        // A hit: a white X (red for the head); a downed foe: a bigger red one.
        if (hit > 0 || kill > 0)
        {
            bool big = kill > 0;
            float r0 = Ui.S(big ? 10 : 8), r1 = Ui.S(big ? 22 : 16);
            var c = big ? Ui.Red : headHit ? new Color(255, 90, 70, 255) : Ui.White;
            float th = Math.Max(1.5f, Ui.S(big ? 3 : 2));
            foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                Raylib.DrawLineEx(new Vector2(cx + dx * r0, cy + dy * r0), new Vector2(cx + dx * r1, cy + dy * r1), th, c);
        }
        // Where the shots that struck came from.
        foreach (var (bearing, left) in damage)
        {
            float rel = ((bearing - f.Player.Yaw) % 360 + 540) % 360 - 180;
            float a = rel - 90f;
            byte alpha = (byte)(220 * Math.Clamp(left / 1.6f, 0, 1));
            Raylib.DrawRing(new Vector2(cx, cy), Ui.S(90), Ui.S(104), a - 16, a + 16, 24, new Color((byte)220, (byte)30, (byte)20, alpha));
        }
    }

    // ------------------------------------------------------------------ widgets

    private void MinimapWidget(HudFrame f, Rectangle r, Widget wd)
    {
        Vector2 c = new(r.X + r.Width / 2, r.Y + r.Height / 2);
        float radius = r.Width / 2;
        if (f.Overlay && !Editing)
        {
            // RimWorld's own window is here, on top: only a ring round it.
            if (wd.Round) Raylib.DrawRing(c, radius, radius + Ui.S(3), 0, 360, 64, Ui.PanelEdge);
            else Raylib.DrawRectangleLinesEx(new Rectangle(r.X - Ui.S(3), r.Y - Ui.S(3), r.Width + Ui.S(6), r.Height + Ui.S(6)), Ui.S(3), Ui.PanelEdge);
            return;
        }
        // No RimWorld window to show (not on Windows, the demo, or still coming): a map of our own.
        if (wd.Round) Raylib.DrawCircleV(c, radius, new Color(16, 17, 19, 210));
        else Raylib.DrawRectangleRec(r, new Color(16, 17, 19, 210));
        Grid grid = f.Grid;
        if (grid.Ready)
        {
            float cells = 34f / wd.Zoom;
            float px = r.Width / (cells * 2);
            int x0 = (int)(f.Player.X - cells) - 1, z0 = (int)(f.Player.Z - cells) - 1;
            for (int z = z0; z <= z0 + cells * 2 + 2; z++)
                for (int x = x0; x <= x0 + cells * 2 + 2; x++)
                {
                    if (!grid.Inside(x, z)) continue;
                    float sx = c.X + (x - f.Player.X) * px, sy = c.Y - (z + 1 - f.Player.Z) * px;
                    if (wd.Round && Vector2.Distance(new Vector2(sx + px / 2, sy + px / 2), c) > radius - px) continue;
                    if (!wd.Round && (sx < r.X || sy < r.Y || sx + px > r.X + r.Width || sy + px > r.Y + r.Height)) continue;
                    Raylib.DrawRectangleRec(new Rectangle(sx, sy, px + 0.6f, px + 0.6f), CellColour(grid, f.Pictures, x, z));
                }
            foreach (Figure fig in f.Figures.All.Values)
            {
                if (fig.Id == f.Player.Playing) continue;
                var at = new Vector2(c.X + (fig.X - f.Player.X) * px, c.Y - (fig.Z - f.Player.Z) * px);
                if (Vector2.Distance(at, c) > radius - px) continue;
                Color dot = fig.Pawn.Dead ? Ui.Dim : fig.Pawn.Hostile ? Ui.Red : fig.Pawn.Faction == "player" ? Ui.Green : Ui.Gold;
                Raylib.DrawCircleV(at, Math.Max(2f, px * 0.55f), dot);
            }
        }
        // You: an arrow the way you face.
        float yaw = f.Player.Yaw * MathF.PI / 180f;
        var fwd = new Vector2(MathF.Sin(yaw), -MathF.Cos(yaw));
        var side = new Vector2(-fwd.Y, fwd.X);
        float s = Ui.S(9);
        Raylib.DrawTriangle(c + fwd * s, c - fwd * s * 0.6f - side * s * 0.6f, c - fwd * s * 0.6f + side * s * 0.6f, Ui.White);
        Raylib.DrawTriangle(c + fwd * s, c - fwd * s * 0.6f + side * s * 0.6f, c - fwd * s * 0.6f - side * s * 0.6f, Ui.White);
        if (wd.Round) Raylib.DrawRing(c, radius, radius + Ui.S(3), 0, 360, 64, Ui.PanelEdge);
        else Raylib.DrawRectangleLinesEx(r, Ui.S(2), Ui.PanelEdge);
        Ui.Centred("N", c.X, r.Y + Ui.S(4), Ui.S(16), Ui.Grey);
    }

    private static Color CellColour(Grid grid, Pictures pictures, int x, int z)
    {
        int i = grid.Index(x, z);
        switch (grid.Codes[i])
        {
            case Code.Rock: return new Color(70, 64, 58, 255);
            case Code.Wall: return pictures.Get(grid.WallSkin[i]) is Pic wall ? Ui.Shade(wall.Average, 0.85f) : new Color(150, 146, 140, 255);
            case Code.Door: return new Color(150, 110, 70, 255);
            case Code.Deep: return new Color(36, 60, 100, 255);
            case Code.Shallow: return new Color(60, 100, 130, 255);
            case Code.Tree: return new Color(52, 90, 44, 255);
            case Code.Machine: return new Color(110, 110, 120, 255);
        }
        return pictures.Get(grid.FloorSkin[i]) is Pic floor ? floor.Average : new Color(110, 92, 66, 255);
    }

    private Texture2D card;
    private int cardRev = -1;

    /// <summary>The colonist's health: RimWorld's own Health tab, as the mod draws it (so a mod
    /// that changes the tab, such as Nice Health Tab, shows here too), fitted into the widget.
    /// Until the first picture comes, the same things laid out as the tab lays them out: the
    /// overview on the left, each hurt part and what is wrong with it on the right.</summary>
    private void StatusWidget(HudFrame f, Rectangle r)
    {
        PawnState? pawn = f.Link.Player;
        if (f.Link.HealthCardRev != cardRev && f.Link.HealthCardPng is byte[] png)
        {
            cardRev = f.Link.HealthCardRev;
            Image image = Raylib.LoadImageFromMemory(".png", png);
            if (image.Width > 0)
            {
                if (card.Id != 0) Raylib.UnloadTexture(card);
                card = Raylib.LoadTextureFromImage(image);
                Raylib.SetTextureFilter(card, TextureFilter.Bilinear);
            }
            Raylib.UnloadImage(image);
        }
        if (card.Id != 0 && pawn != null && f.Link.HealthCardPawn == pawn.Id)
        {
            float k = Math.Min(r.Width / card.Width, r.Height / card.Height);
            var dest = new Rectangle(r.X, r.Y, card.Width * k, card.Height * k);
            Color tint = faceFlash > 0 ? new Color(255, 200, 196, 245) : new Color(255, 255, 255, 245);
            Raylib.DrawTexturePro(card, new Rectangle(0, 0, card.Width, card.Height), dest, Vector2.Zero, 0, tint);
            return;
        }
        HealthList(f, r, pawn);
    }

    /// <summary>The Health tab's layout, from the <c>status</c> report.</summary>
    private void HealthList(HudFrame f, Rectangle r, PawnState? pawn)
    {
        Ui.Panel(r, new Color(21, 25, 29, 235));
        PawnStatus? st = f.Link.Status is PawnStatus s && pawn != null && s.Pawn == pawn.Id ? s : null;
        float k = r.Height / BaseSize["status"].Y;
        float pad = 12 * k, size = 15 * k, line = 21 * k;
        float split = r.X + r.Width * 0.4f;
        float y = r.Y + pad;
        string name = pawn?.Name ?? "Colonist";
        Ui.Text(name, r.X + pad, y, 19 * k, Ui.White);
        y += 28 * k;
        Raylib.DrawRectangleRec(new Rectangle(split, r.Y + pad, 1, r.Height - pad * 2), new Color(255, 255, 255, 40));
        if (st == null)
        {
            Ui.Text("Waiting for RimWorld...", r.X + pad, y, size, Ui.Dim);
            return;
        }
        // Left: the overview.
        void Row(string label, string value, Color color)
        {
            if (y > r.Y + r.Height - line) return;
            Ui.Text(label, r.X + pad, y, size, Ui.Grey);
            Ui.Right(value, split - pad, y, size, color);
            y += line;
        }
        Row("Health", $"{st.Health * 100:0}%", Ui.Health(st.Health));
        Row("Pain", st.Pain < 0.01f ? "None" : st.Pain < 0.15f ? "Minor" : st.Pain < 0.4f ? "Serious" : st.Pain < 0.8f ? "Intense" : "Extreme",
            st.Pain < 0.15f ? Ui.Grey : st.Pain < 0.4f ? Ui.Orange : Ui.Red);
        if (st.Bleeding > 0)
            Row("Bleeding", st.BleedHours is float hours ? $"{hours:0.#}h left" : $"{st.Bleeding * 100:0}%/day", Ui.Red);
        if (st.Mood is float mood) Row("Mood", $"{mood * 100:0}%", mood < 0.25f ? Ui.Red : mood < 0.45f ? Ui.Orange : Ui.Grey);
        y += line * 0.3f;
        foreach (var (cap, value) in st.Capacities)
            Row(Title(cap), value <= 0 ? "None" : $"{value * 100:0}%", value >= 0.995f ? Ui.Grey : value >= 0.7f ? new Color(196, 206, 80, 255) : value >= 0.4f ? Ui.Orange : Ui.Red);
        // Right: each part and what is wrong with it.
        float rx = split + pad, ry = r.Y + pad + 28 * k;
        var byPart = st.Conditions.GroupBy(c => c.part ?? "Whole body").ToList();
        if (byPart.Count == 0) Ui.Text("No health conditions", rx, ry, size, Ui.Dim);
        foreach (var group in byPart)
        {
            if (ry > r.Y + r.Height - line) break;
            string part = Title(group.Key.Replace(' ', '_'));
            float partHealth = st.Parts.TryGetValue(group.Key.Replace(' ', '_').ToLowerInvariant(), out var ph) ? ph.health : 1f;
            Ui.Text(part, rx, ry, size, Ui.Health(partHealth));
            float cy = ry;
            foreach (var (label, _, bleeding) in group)
            {
                if (cy > r.Y + r.Height - line) break;
                string text = bleeding ? label + "  (bleeding)" : label;
                Ui.Right(text, r.X + r.Width - pad, cy, size, bleeding ? Ui.Red : Ui.White);
                cy += line;
            }
            ry = Math.Max(cy, ry + line);
        }
    }

    private static string Title(string key) => string.Join(' ', key.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>The weapon in hand: RimWorld's picture of it, its name, the rounds left of its burst.</summary>
    private void WeaponWidget(HudFrame f, Rectangle r)
    {
        Ui.Panel(r);
        float k = r.Height / BaseSize["weapon"].Y;
        PawnState? pawn = f.Link.Player;
        Gun gun = f.Gun;
        var icon = new Rectangle(r.X + 10 * k, r.Y + 10 * k, 140 * k, r.Height - 20 * k);
        if (pawn?.Held is Held held && f.Pictures.Get(held.Tex) is Pic pic) Ui.Fit(f.Atlas, pic, icon, Color.White);
        string name = f.Link.Snap?.PlayerWeapon ?? Hud.Pretty(pawn?.Weapon) ?? "bare hands";
        float tx = icon.X + icon.Width + 10 * k, right = r.X + r.Width - 12 * k;
        Ui.Text(Capital(name), tx, r.Y + 8 * k, 18 * k, Ui.White);
        switch (gun.Kind)
        {
            case Gun.Mode.Gun:
                string count = gun.Cycle > 0 ? "-" : gun.Clip.ToString();
                Ui.Right(count, right - Ui.Width($" / {gun.Burst}", 22 * k), r.Y + 38 * k, 44 * k, gun.Clip == 0 || gun.Cycle > 0 ? Ui.Orange : Ui.White);
                Ui.Right($" / {gun.Burst}", right, r.Y + 54 * k, 22 * k, Ui.Grey);
                Ui.Text(gun.Burst > 1 ? $"Burst {gun.Burst}" : "Single", tx, r.Y + 40 * k, 15 * k, Ui.Grey);
                if (f.Aim > 0.5f) Ui.Text($"x{gun.Zoom:0.#}", tx, r.Y + 60 * k, 15 * k, Ui.Gold);
                if (gun.Cycle > 0)
                {
                    float done = 1f - gun.Cycle / Math.Max(gun.CycleLength, 0.01f);
                    Ui.Bar(tx, r.Y + r.Height - 16 * k, right - tx, 6 * k, done, Ui.Gold);
                }
                break;
            case Gun.Mode.Melee:
                Ui.Text("Melee: click, V", tx, r.Y + 44 * k, 16 * k, Ui.Grey);
                if (gun.Wait > 0) Ui.Bar(tx, r.Y + r.Height - 16 * k, right - tx, 6 * k, 1 - gun.Wait / 0.9f, Ui.Gold);
                break;
            case Gun.Mode.Throw:
                Ui.Text(gun.Wait > 0 ? "Readying..." : "Throw: click or G", tx, r.Y + 44 * k, 16 * k, gun.Wait > 0 ? Ui.Orange : Ui.Grey);
                break;
            default:
                Ui.Text("Fists: click or V", tx, r.Y + 44 * k, 16 * k, Ui.Grey);
                break;
        }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>A heading strip: where you face, and where everyone on the map is from you.</summary>
    private void CompassWidget(HudFrame f, Rectangle r)
    {
        Raylib.DrawRectangleRec(r, new Color(16, 17, 19, 140));
        float k = r.Height / BaseSize["compass"].Y;
        float span = 120f, cx = r.X + r.Width / 2;
        float yaw = f.Player.Yaw;
        float X(float bearing) => cx + (((bearing - yaw) % 360 + 540) % 360 - 180) / span * r.Width;
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        for (int d = 0; d < 360; d += 15)
        {
            float x = X(d);
            if (x < r.X || x > r.X + r.Width) continue;
            bool major = d % 45 == 0;
            Raylib.DrawRectangleRec(new Rectangle(x - 0.5f * k, r.Y + r.Height - (major ? 12 : 7) * k, Math.Max(1, k), (major ? 12 : 7) * k), Ui.Grey);
            if (major) Ui.Centred(names[d / 45], x, r.Y + 2 * k, d % 90 == 0 ? 20 * k : 15 * k, d == 0 ? Ui.Red : Ui.White);
        }
        foreach (Figure fig in f.Figures.All.Values)
        {
            if (fig.Id == f.Player.Playing || fig.Pawn.Dead) continue;
            if (!fig.Pawn.Hostile && fig.Pawn.Faction != "player") continue;
            float dx = fig.X - f.Player.X, dz = fig.Z - f.Player.Z;
            if (dx * dx + dz * dz > 60 * 60) continue;
            float x = Math.Clamp(X(Figures.Heading(dx, dz)), r.X + 4, r.X + r.Width - 4);
            Color c = fig.Pawn.Hostile ? (fig.Pawn.Downed ? Ui.Dim : Ui.Red) : Ui.Green;
            float s = 6 * k, y = r.Y + r.Height - 2 * k;
            Raylib.DrawTriangle(new Vector2(x, y - s * 1.6f), new Vector2(x - s, y), new Vector2(x + s, y), c);
            Raylib.DrawTriangle(new Vector2(x, y - s * 1.6f), new Vector2(x + s, y), new Vector2(x - s, y), c);
        }
        Raylib.DrawRectangleRec(new Rectangle(cx - k, r.Y, 2 * k, r.Height), Ui.Gold);
    }

    private void MessagesWidget(Rectangle r)
    {
        float k = r.Height / BaseSize["messages"].Y;
        float y = r.Y;
        foreach (var (text, left, color) in messages)
        {
            Ui.Centred(text, r.X + r.Width / 2, y, 20 * k, Ui.Fade(color, Math.Min(1f, left * 2f)));
            y += 24 * k;
        }
        if (Editing && messages.Count == 0) Ui.Centred("Messages show here", r.X + r.Width / 2, y, 20 * k, Ui.Dim);
    }

    private void PromptWidget(HudFrame f, Rectangle r)
    {
        string? prompt = Editing ? f.Prompt ?? "[E] Open door" : f.Prompt;
        if (prompt == null) return;
        float k = r.Height / BaseSize["prompt"].Y;
        Ui.Centred(prompt, r.X + r.Width / 2, r.Y + 10 * k, 22 * k, Ui.White);
        if (f.LookingAt != null && !Editing) Ui.Centred(f.LookingAt, r.X + r.Width / 2, r.Y - 20 * k, 17 * k, Ui.Grey);
    }

    // ------------------------------------------------------------------ edit mode

    private void EditFrame(string id, Rectangle r, Widget wd)
    {
        bool over = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color edge = dragging == id ? Ui.Gold : over ? Ui.White : Ui.Grey;
        Raylib.DrawRectangleLinesEx(r, Math.Max(1, Ui.S(2)), edge);
        if (!wd.Shown) Raylib.DrawRectangleRec(r, new Color(0, 0, 0, 150));
        string label = Names[id] + (wd.Shown ? "" : " (hidden)") + (id == "minimap" ? wd.Round ? "  round" : "  square" : "");
        Ui.Text(label, r.X + Ui.S(4), r.Y + r.Height + Ui.S(3), Ui.S(15), edge);
    }

    private static void EditHelp(int w, int h)
    {
        string[] lines =
        {
            "EDIT HUD",
            "Widgets: drag to move  -  Wheel to resize  -  Right click to hide or show  -  R to reset",
            "Minimap: middle click round or square, Shift + wheel to zoom",
            "Weapon: drag it, right-drag to turn, wheel to size; drag the hands and muzzle; panel on the right",
            "F2 or Esc when done",
        };
        float y = h * 0.2f;
        for (int i = 0; i < lines.Length; i++)
        {
            Ui.Centred(lines[i], w / 2f, y, Ui.S(i == 0 ? 30 : 18), i == 0 ? Ui.Gold : Ui.White);
            y += Ui.S(i == 0 ? 40 : 24);
        }
    }

    /// <summary>The mouse while editing: drag, resize, hide, round, zoom, reset.</summary>
    public void Edit(int w, int h)
    {
        if (!Editing) return;
        Vector2 mouse = Raylib.GetMousePosition();
        string? over = rects.Where(r => Raylib.CheckCollisionPointRec(mouse, r.Value)).Select(r => r.Key).LastOrDefault();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && over != null)
        {
            dragging = over;
            grab = mouse - new Vector2(rects[over].X, rects[over].Y);
        }
        if (dragging != null && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            Widget wd = settings.Hud[dragging];
            Vector2 at = mouse - grab;
            wd.X = Math.Clamp(at.X / w, 0, 0.98f);
            wd.Y = Math.Clamp(at.Y / h, 0, 0.98f);
            LayoutChanged = true;
        }
        if (Raylib.IsMouseButtonReleased(MouseButton.Left)) dragging = null;
        float wheel = Raylib.GetMouseWheelMove();
        if (over != null && wheel != 0)
        {
            Widget wd = settings.Hud[over];
            if (over == "minimap" && (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)))
                wd.Zoom = Math.Clamp(wd.Zoom * (wheel > 0 ? 1.12f : 1 / 1.12f), 0.3f, 4f);
            else
                wd.Scale = Math.Clamp(wd.Scale * (wheel > 0 ? 1.08f : 1 / 1.08f), 0.4f, 3f);
            LayoutChanged = true;
        }
        if (Raylib.IsMouseButtonPressed(MouseButton.Right) && over != null)
        {
            settings.Hud[over].Shown = !settings.Hud[over].Shown;
            LayoutChanged = true;
        }
        if (Raylib.IsMouseButtonPressed(MouseButton.Middle) && over == "minimap")
        {
            settings.Hud["minimap"].Round = !settings.Hud["minimap"].Round;
            LayoutChanged = true;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            settings.Hud = Settings.DefaultHud();
            LayoutChanged = true;
        }
    }

    /// <summary>A def name as words: <c>Gun_AssaultRifle</c> is "assault rifle".</summary>
    public static string? Pretty(string? def)
    {
        if (def == null) return null;
        string s = def;
        foreach (string prefix in new[] { "Gun_", "MeleeWeapon_", "Weapon_" })
            if (s.StartsWith(prefix)) s = s[prefix.Length..];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i] == '_' ? ' ' : char.ToLowerInvariant(s[i]));
        }
        return sb.ToString();
    }
}
