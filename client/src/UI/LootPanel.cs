using System.Numerics;
using RimFps.Net;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// F: the colonist's gear, against what lies within reach (the ground, corpses, the downed), as
/// RimWorld reports it in <c>loot</c>. A click takes a thing (a weapon goes in hand, apparel is
/// worn, the rest carried, by RimWorld's own rules) or drops one.
/// </summary>
public sealed class LootPanel
{
    public bool Open;
    private readonly List<(Rectangle box, string item, bool take)> rows = new();

    public void Toggle() => Open = !Open;

    public void Draw(int w, int h, Link link, Vector2 mouse, bool click, Action<string> send, Hud hud)
    {
        rows.Clear();
        if (!Open) return;
        Loot? loot = link.Loot;
        if (loot == null || link.Player == null || loot.Pawn != link.Player.Id)
        {
            Ui.Centred("Nothing to loot yet", w / 2f, h / 2f, Ui.S(22), Ui.Grey);
            return;
        }
        float pw = Math.Min(w - Ui.S(40), Ui.S(900)), ph = Math.Min(h - Ui.S(80), Ui.S(560));
        var panel = new Rectangle((w - pw) / 2, (h - ph) / 2, pw, ph);
        Ui.Panel(panel, new Color(22, 23, 25, 235));
        float pad = Ui.S(16), line = Ui.S(26), size = Ui.S(18);
        float col = (pw - pad * 3) / 2;
        float lx = panel.X + pad, rx = panel.X + pad * 2 + col;
        Ui.Text("Gear", lx, panel.Y + pad, Ui.S(24), Ui.White);
        Ui.Right($"{loot.Mass:0.#} / {loot.Capacity:0.#} kg", lx + col, panel.Y + pad + Ui.S(4), size, loot.Mass > loot.Capacity ? Ui.Red : Ui.Grey);
        Ui.Text("Nearby", rx, panel.Y + pad, Ui.S(24), Ui.White);
        Raylib.DrawRectangleRec(new Rectangle(panel.X + pad * 1.5f + col, panel.Y + pad, 1, ph - pad * 2), Ui.PanelEdge);
        float bottom = panel.Y + ph - pad - line * 1.5f;
        float y = panel.Y + pad + Ui.S(40);
        foreach (LootItem item in loot.Kit)
        {
            if (y > bottom) break;
            Row(lx, y, col, item, take: false, mouse, size);
            y += line;
        }
        if (loot.Kit.Count == 0) Ui.Text("(nothing)", lx, y, size, Ui.Dim);
        y = panel.Y + pad + Ui.S(40);
        bool any = false;
        foreach (LootSource source in loot.Nearby)
        {
            if (y > bottom) break;
            if (source.Items.Count == 0) continue;
            any = true;
            Ui.Text(source.Label, rx, y, size, Ui.Gold);
            y += line;
            foreach (LootItem item in source.Items)
            {
                if (y > bottom) break;
                Row(rx + Ui.S(10), y, col - Ui.S(10), item, take: true, mouse, size);
                y += line;
            }
        }
        if (!any) Ui.Text("(nothing in reach)", rx, y, size, Ui.Dim);
        Ui.Centred("Click to take or drop  -  F to close", w / 2f, panel.Y + ph - pad - Ui.S(20), Ui.S(16), Ui.Dim);
        if (!click) return;
        foreach (var (box, item, take) in rows)
        {
            if (!Raylib.CheckCollisionPointRec(mouse, box)) continue;
            send(take ? Out.LootTake(loot.Pawn, item) : Out.LootDrop(loot.Pawn, item));
            hud.Message(take ? "Taking..." : "Dropping...", Ui.Grey, 1f);
            break;
        }
    }

    private void Row(float x, float y, float width, LootItem item, bool take, Vector2 mouse, float size)
    {
        var box = new Rectangle(x - Ui.S(4), y - Ui.S(2), width, Ui.S(24));
        bool over = Raylib.CheckCollisionPointRec(mouse, box);
        if (over) Raylib.DrawRectangleRec(box, new Color(74, 76, 80, 200));
        Color mark = item.Kind switch { "weapon" => Ui.Red, "apparel" => Ui.Blue, _ => Ui.Grey };
        Raylib.DrawRectangleRec(new Rectangle(x, y + Ui.S(6), Ui.S(8), Ui.S(8)), mark);
        string label = item.Count > 1 && !item.Label.Contains(" x") ? $"{item.Label} x{item.Count}" : item.Label;
        label = char.ToUpperInvariant(label[0]) + label[1..];
        float room = width - Ui.S(80);
        while (label.Length > 4 && Ui.Width(label, size) > room) label = label[..^1];
        Ui.Text(label, x + Ui.S(16), y, size, over ? Ui.Gold : Ui.White);
        Ui.Right($"{item.Mass:0.#} kg", x + width - Ui.S(8), y, size, Ui.Dim);
        rows.Add((box, item.Id, take));
    }
}
