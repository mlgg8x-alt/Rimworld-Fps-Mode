using System.Numerics;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// Playing on a phone (the Android app): a move stick wherever the left thumb lands on the left
/// part of the screen, looking by dragging anywhere else, and round buttons on the right for
/// what the keyboard and mouse do on a computer. Each finger keeps the job it started with until
/// it lifts, so a thumb on the stick never fires or looks. A keyboard and mouse still work too.
/// </summary>
public sealed class TouchControls
{
    public enum Button { Fire, Aim, Jump, Reload, Melee, Use, Loot, Grenade, Menu, Map }

    /// <summary>Stick, -1 to 1: x right, y forward.</summary>
    public Vector2 Move { get; private set; }
    /// <summary>Screen pixels dragged to look this frame.</summary>
    public Vector2 Look { get; private set; }
    /// <summary>Held now.</summary>
    public bool Fire { get; private set; }
    /// <summary>Aiming down the sights: the aim button toggles it.</summary>
    public bool Aiming { get; set; }
    /// <summary>Some finger is on the screen.</summary>
    public bool Touching { get; private set; }

    private readonly HashSet<Button> pressed = new();
    private readonly Dictionary<int, (string job, Vector2 start, Vector2 last, Button? button)> fingers = new();

    /// <summary>Pressed this frame.</summary>
    public bool Pressed(Button button) => pressed.Contains(button);

    private static readonly (Button button, string label, float x, float y, float r)[] Layout =
    {
        // Share of the width (from the right edge) and height, radius as a share of the height.
        (Button.Fire, "FIRE", 0.11f, 0.70f, 0.095f),
        (Button.Aim, "AIM", 0.25f, 0.82f, 0.065f),
        (Button.Jump, "JUMP", 0.07f, 0.45f, 0.06f),
        (Button.Reload, "R", 0.20f, 0.52f, 0.05f),
        (Button.Melee, "MELEE", 0.32f, 0.62f, 0.055f),
        (Button.Use, "USE", 0.36f, 0.42f, 0.05f),
        (Button.Grenade, "G", 0.24f, 0.33f, 0.045f),
        (Button.Loot, "LOOT", 0.42f, 0.86f, 0.045f),
        (Button.Map, "MAP", 0.04f, 0.22f, 0.035f),
        (Button.Menu, "MENU", 0.04f, 0.08f, 0.035f),
    };

    private static (Vector2 at, float r) Place((Button button, string label, float x, float y, float r) b, int w, int h) =>
        (new Vector2(w - b.x * w, b.y * h), b.r * h);

    /// <summary>Reads the fingers. <paramref name="live"/>: playing (else only the menu button counts).</summary>
    public void Update(int w, int h, bool live)
    {
        pressed.Clear();
        Look = Vector2.Zero;
        Move = Vector2.Zero;
        Fire = false;
        int count = Raylib.GetTouchPointCount();
        Touching = count > 0;
        var seen = new HashSet<int>();
        float stickRadius = h * 0.13f;
        for (int i = 0; i < count; i++)
        {
            int id = Raylib.GetTouchPointId(i);
            Vector2 at = Raylib.GetTouchPosition(i);
            seen.Add(id);
            if (!fingers.TryGetValue(id, out var finger))
            {
                // A new finger: a button under it, else the stick on the left, else looking.
                Button? hit = null;
                foreach (var b in Layout)
                {
                    var (centre, r) = Place(b, w, h);
                    if (Vector2.Distance(at, centre) <= r * 1.15f && (live || b.button == Button.Menu)) hit = b.button;
                }
                string job = hit != null ? "button" : at.X < w * 0.4f ? "stick" : "look";
                finger = (job, at, at, hit);
                if (hit is Button pushed)
                {
                    pressed.Add(pushed);
                    if (pushed == Button.Aim) Aiming = !Aiming;
                }
            }
            switch (finger.job)
            {
                case "stick":
                    Vector2 off = (at - finger.start) / stickRadius;
                    if (off.Length() > 1f) off = Vector2.Normalize(off);
                    Move = new Vector2(off.X, -off.Y);
                    break;
                case "look":
                    Look += at - finger.last;
                    break;
                case "button":
                    if (finger.button == Button.Fire) Fire = true;
                    // Dragging off the fire button looks around while firing.
                    if (finger.button == Button.Fire) Look += (at - finger.last) * 0.6f;
                    break;
            }
            fingers[id] = (finger.job, finger.start, at, finger.button);
        }
        foreach (int gone in fingers.Keys.Where(id => !seen.Contains(id)).ToList()) fingers.Remove(gone);
        if (!live)
        {
            Move = Vector2.Zero;
            Look = Vector2.Zero;
            Fire = false;
        }
    }

    /// <summary>The stick and buttons, see-through, over the view.</summary>
    public void Draw(int w, int h, bool live)
    {
        foreach (var b in Layout)
        {
            if (!live && b.button != Button.Menu) continue;
            var (centre, r) = Place(b, w, h);
            bool down = b.button == Button.Fire && Fire || b.button == Button.Aim && Aiming
                || fingers.Values.Any(f => f.button == b.button);
            Raylib.DrawCircleV(centre, r, new Color(20, 22, 24, down ? 170 : 90));
            Raylib.DrawCircleLinesV(centre, r, new Color(236, 236, 236, down ? 220 : 120));
            float size = Math.Max(10f, r * (b.label.Length > 3 ? 0.42f : 0.6f));
            Ui.Centred(b.label, centre.X, centre.Y - size * 0.55f, size, new Color(236, 236, 236, down ? 255 : 170));
        }
        if (!live) return;
        foreach (var f in fingers.Values.Where(f => f.job == "stick"))
        {
            float r = h * 0.13f;
            Raylib.DrawCircleV(f.start, r, new Color(20, 22, 24, 80));
            Raylib.DrawCircleLinesV(f.start, r, new Color(236, 236, 236, 110));
            Vector2 knob = f.last - f.start;
            if (knob.Length() > r) knob = Vector2.Normalize(knob) * r;
            Raylib.DrawCircleV(f.start + knob, r * 0.4f, new Color(236, 236, 236, 140));
        }
    }
}
