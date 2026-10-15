using System.Numerics;
using RimFps.Game;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// The weapon part of the HUD's edit mode: the weapon in hand held still, at the hip or aimed,
/// to be moved about with the mouse, all flat on the screen. Drag it to move it, right-drag
/// (or Shift + wheel) to turn it, wheel to size it; drag either hand, or the muzzle, along it.
/// A panel beside it has the same as sliders, the number of hands, mirroring, and resets.
/// Each weapon keeps its own pose (by its RimWorld picture), and can hand it to every weapon of
/// its kind. Nothing needs setting: every weapon is placed from its picture to start with.
/// </summary>
public sealed class WeaponEditor
{
    private enum Grab { None, Body, Turn, Grip, Fore, Muzzle, Slider }

    /// <summary>Editing the aimed pose rather than the hip.</summary>
    public bool AimTab;
    /// <summary>The mouse is the editor's (on the weapon, a handle or the panel, or dragging):
    /// the HUD leaves it alone.</summary>
    public bool Captured { get; private set; }
    private Grab grab;
    private int slider;
    private Vector2 last;
    private Rectangle panel;

    private sealed record Slider(string Label, Func<WeaponPose, bool, float> Get, Action<WeaponPose, bool, float> Set, float Min, float Max, string Format);

    private static readonly Slider[] Sliders =
    {
        new("Points at", (p, a) => a ? p.AimPoint : p.Point, (p, a, v) => { if (a) p.AimPoint = v; else p.Point = v; }, 90, 450, "0"),
        new("Left / right", (p, a) => a ? p.AimGripX : p.GripX, (p, a, v) => { if (a) p.AimGripX = v; else p.GripX = v; }, 0, 1, "0.00"),
        new("Up / down", (p, a) => a ? p.AimGripY : p.GripY, (p, a, v) => { if (a) p.AimGripY = v; else p.GripY = v; }, 0.3f, 1.2f, "0.00"),
        new("Size", (p, _) => p.Size, (p, _, v) => p.Size = v, 0.3f, 3f, "0.00"),
        new("Edge", (p, _) => p.Thickness, (p, _, v) => p.Thickness = v, 0f, 4f, "0.0"),
        new("Hand size", (p, _) => p.HandSize, (p, _, v) => p.HandSize = v, 0.3f, 3f, "0.00"),
    };

    /// <summary>The weapon's own pose, made from what it uses now if it has none yet.</summary>
    private static WeaponPose Own(Settings settings, Hands hands)
    {
        string name = hands.ShownName;
        if (!settings.Weapons.TryGetValue(name, out WeaponPose? own))
            settings.Weapons[name] = own = settings.PoseFor(name, hands.Shown!.Melee).Copy();
        return own;
    }

    private static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)
                && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y + 1e-6f) + poly[i].X)
                inside = !inside;
        return inside;
    }

    private static float Wrap(float angle) => ((angle - 90f) % 360f + 360f) % 360f + 90f;

    /// <summary>Draws the handles and the panel, and takes the mouse. <paramref name="hudBusy"/>:
    /// the mouse is on a HUD widget, which comes first.</summary>
    public void Draw(Hands hands, Settings settings, int w, int h, bool hudBusy)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        Vector2 delta = mouse - last;
        last = mouse;
        bool pressL = Raylib.IsMouseButtonPressed(MouseButton.Left), pressR = Raylib.IsMouseButtonPressed(MouseButton.Right);
        bool downL = Raylib.IsMouseButtonDown(MouseButton.Left), downR = Raylib.IsMouseButtonDown(MouseButton.Right);
        if (!downL && !downR) grab = Grab.None;

        float pw = Ui.S(340), line = Ui.S(30), pad = Ui.S(14);
        float ph = pad * 2 + Ui.S(64) + line * Sliders.Length + Ui.S(150);
        panel = new Rectangle(w - pw - Ui.S(16), Math.Max(Ui.S(70), h * 0.14f), pw, ph);
        Ui.Panel(panel, new Color(22, 23, 25, 225));
        float x = panel.X + pad, y = panel.Y + pad, width = pw - pad * 2;
        bool overPanel = Raylib.CheckCollisionPointRec(mouse, panel);

        WeaponModel? model = hands.Shown;
        if (model == null)
        {
            Ui.Text("WEAPON", x, y, Ui.S(22), Ui.Gold);
            Ui.Text("Play a colonist holding a gun or a", x, y + Ui.S(40), Ui.S(17), Ui.Grey);
            Ui.Text("melee weapon to set how they hold it.", x, y + Ui.S(62), Ui.S(17), Ui.Grey);
            Captured = overPanel;
            return;
        }
        bool melee = model.Melee;
        if (melee) AimTab = false;
        WeaponPose shown = settings.PoseFor(hands.ShownName, melee);
        bool own = settings.Weapons.ContainsKey(hands.ShownName);
        string title = hands.ShownName.Split('/', '\\').LastOrDefault() ?? "weapon";
        Ui.Text("WEAPON", x, y, Ui.S(22), Ui.Gold);
        Ui.Right(own ? "own pose" : $"pose of all {(melee ? "melee" : "guns")}", x + width, y + Ui.S(4), Ui.S(15), Ui.Dim);
        Ui.Text(title.Length > 30 ? title[..30] : title, x, y + Ui.S(26), Ui.S(17), Ui.White);
        y += Ui.S(56);

        bool Button(Rectangle box, string label, bool on)
        {
            bool over = Raylib.CheckCollisionPointRec(mouse, box);
            Raylib.DrawRectangleRec(box, on ? new Color(92, 80, 40, 230) : over ? new Color(70, 72, 76, 220) : new Color(44, 46, 50, 220));
            Raylib.DrawRectangleLinesEx(box, 1, Ui.PanelEdge);
            Ui.Centred(label, box.X + box.Width / 2, box.Y + (box.Height - Ui.S(16)) / 2 - Ui.S(1), Ui.S(16), on ? Ui.Gold : Ui.White);
            return over && pressL && grab == Grab.None;
        }
        if (!melee)
        {
            float half = (width - Ui.S(8)) / 2;
            if (Button(new Rectangle(x, y, half, Ui.S(28)), "At the hip", !AimTab)) AimTab = false;
            if (Button(new Rectangle(x + half + Ui.S(8), y, half, Ui.S(28)), "Aiming", AimTab)) AimTab = true;
        }
        else Ui.Text("Held ready to swing", x, y + Ui.S(4), Ui.S(16), Ui.Grey);
        y += Ui.S(40);

        // Sliders.
        for (int i = 0; i < Sliders.Length; i++)
        {
            Slider s = Sliders[i];
            var box = new Rectangle(x + width * 0.42f, y + i * line + line * 0.3f, width * 0.4f, line * 0.4f);
            float v = s.Get(shown, AimTab);
            if (i == 0) v = Wrap(v);
            float share = Math.Clamp((v - s.Min) / (s.Max - s.Min), 0, 1);
            bool over = Raylib.CheckCollisionPointRec(mouse, new Rectangle(box.X - 6, box.Y - 8, box.Width + 12, box.Height + 16));
            Ui.Text(s.Label, x, y + i * line + Ui.S(4), Ui.S(16), over || grab == Grab.Slider && slider == i ? Ui.Gold : Ui.White);
            Raylib.DrawRectangleRec(box, new Color(10, 10, 12, 200));
            Raylib.DrawRectangleRec(new Rectangle(box.X, box.Y, box.Width * share, box.Height), new Color(150, 130, 70, 230));
            Raylib.DrawCircleV(new Vector2(box.X + box.Width * share, box.Y + box.Height / 2), Ui.S(7), Ui.Gold);
            Ui.Right(v.ToString(s.Format), x + width, y + i * line + Ui.S(4), Ui.S(15), Ui.Grey);
            if (over && pressL && grab == Grab.None)
            {
                grab = Grab.Slider;
                slider = i;
            }
            if (grab == Grab.Slider && slider == i && downL)
                s.Set(Own(settings, hands), AimTab, s.Min + (s.Max - s.Min) * Math.Clamp((mouse.X - box.X) / box.Width, 0, 1));
        }
        y += line * Sliders.Length + Ui.S(8);

        // Hands, mirroring, resets.
        float third = (width - Ui.S(16)) / 3;
        string count = shown.HandCount switch { 1 => "One hand", 2 => "Two hands", _ => "Hands: auto" };
        if (Button(new Rectangle(x, y, third, Ui.S(28)), count, false))
            Own(settings, hands).HandCount = (shown.HandCount + 1) % 3;
        string flip = shown.Flip switch { 1 => "Mirrored", 2 => "Not mirrored", _ => "Mirror: auto" };
        if (Button(new Rectangle(x + third + Ui.S(8), y, third, Ui.S(28)), flip, false))
            Own(settings, hands).Flip = (shown.Flip + 1) % 3;
        if (Button(new Rectangle(x + (third + Ui.S(8)) * 2, y, third, Ui.S(28)), "Auto hands", false))
        {
            WeaponPose p = Own(settings, hands);
            p.Grip = p.Fore = p.Muzzle = null;
        }
        y += Ui.S(36);
        string kind = melee ? "*melee" : "*gun";
        float wide = (width - Ui.S(8)) * 0.68f;
        if (Button(new Rectangle(x, y, wide, Ui.S(28)), $"Use for all {(melee ? "melee" : "guns")}", false))
        {
            settings.Weapons[kind] = shown.Copy();
            // This weapon follows the pose of all its kind again.
            settings.Weapons.Remove(hands.ShownName);
        }
        // Reset: this weapon back to the pose of all its kind; pressed again, that back to the start.
        if (Button(new Rectangle(x + wide + Ui.S(8), y, width - wide - Ui.S(8), Ui.S(28)), "Reset", false))
            if (!settings.Weapons.Remove(hands.ShownName)) settings.Weapons.Remove(kind);
        y += Ui.S(38);
        Ui.Text("Drag: move   Wheel: size", x, y, Ui.S(14), Ui.Dim);
        Ui.Text("Right-drag or Shift+wheel: turn", x, y + Ui.S(18), Ui.S(14), Ui.Dim);
        Ui.Text("Drag the hands and the muzzle (orange)", x, y + Ui.S(36), Ui.S(14), Ui.Dim);

        // The weapon itself: its outline, its handles.
        Hands.Placed placed = hands.ShownAt;
        Vector2[] outline = placed.Corners();
        for (int i = 0; i < 4; i++) Raylib.DrawLineEx(outline[i], outline[(i + 1) % 4], Math.Max(1f, Ui.S(1.5f)), new Color(232, 196, 86, 150));
        var handles = new List<(Grab what, Vector2 at, Color color)> { (Grab.Grip, hands.ShownGrip, Ui.Gold) };
        if (hands.ShownFore is Vector2 fore) handles.Add((Grab.Fore, fore, Ui.Gold));
        if (hands.ShownMuzzle is Vector2 muzzle) handles.Add((Grab.Muzzle, muzzle, Ui.Orange));
        float pick = Ui.S(20);
        Grab overHandle = Grab.None;
        foreach (var (what, at, color) in handles)
        {
            bool over = Vector2.Distance(mouse, at) < pick;
            if (over && overHandle == Grab.None) overHandle = what;
            Raylib.DrawCircleLinesV(at, pick * (over || grab == what ? 1f : 0.75f), color);
            Raylib.DrawCircleV(at, Ui.S(3), color);
        }
        bool overWeapon = Inside(mouse, outline);
        bool free = !overPanel && !hudBusy;
        if (grab == Grab.None && free)
        {
            if (pressL && overHandle != Grab.None) grab = overHandle;
            else if (pressL && overWeapon) grab = Grab.Body;
            else if (pressR && (overWeapon || overHandle != Grab.None)) grab = Grab.Turn;
        }

        // Dragging.
        WeaponPose pose;
        switch (grab)
        {
            case Grab.Body when downL:
                pose = Own(settings, hands);
                if (AimTab)
                {
                    pose.AimGripX += delta.X / w;
                    pose.AimGripY += delta.Y / h;
                }
                else
                {
                    pose.GripX += delta.X / w;
                    pose.GripY += delta.Y / h;
                }
                break;
            case Grab.Turn when downR:
            {
                // Turned about the grip, following the mouse round it.
                pose = Own(settings, hands);
                Vector2 from = mouse - delta - placed.At, to = mouse - placed.At;
                if (from.LengthSquared() < 25f || to.LengthSquared() < 25f) break;
                float turn = (MathF.Atan2(to.Y, to.X) - MathF.Atan2(from.Y, from.X)) * 180f / MathF.PI;
                turn = Figures.AngleDiff(0, turn);
                if (AimTab) pose.AimPoint = Wrap(pose.AimPoint + turn);
                else pose.Point = Wrap(pose.Point + turn);
                break;
            }
            case Grab.Grip or Grab.Fore or Grab.Muzzle when downL:
            {
                pose = Own(settings, hands);
                Vector2 at = placed.ToPicture(mouse);
                var point = new[] { Math.Clamp(at.X, -0.3f, 1.3f), Math.Clamp(at.Y, -0.3f, 1.3f) };
                if (grab == Grab.Grip)
                {
                    // The weapon is placed by its grip: the place moves with the grip, so the
                    // weapon stays put and only the hand slides along it.
                    pose.Grip = point;
                    if (AimTab) (pose.AimGripX, pose.AimGripY) = (mouse.X / w, mouse.Y / h);
                    else (pose.GripX, pose.GripY) = (mouse.X / w, mouse.Y / h);
                }
                else if (grab == Grab.Fore) pose.Fore = point;
                else pose.Muzzle = point;
                break;
            }
        }

        // The wheel on the weapon.
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && free && (overWeapon || overHandle != Grab.None))
        {
            pose = Own(settings, hands);
            bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            if (shift)
            {
                if (AimTab) pose.AimPoint = Wrap(pose.AimPoint + wheel * 4f);
                else pose.Point = Wrap(pose.Point + wheel * 4f);
            }
            else pose.Size = Math.Clamp(pose.Size * MathF.Pow(1.06f, wheel), 0.3f, 3f);
        }
        Captured = overPanel || grab != Grab.None || free && (overWeapon || overHandle != Grab.None);
    }
}
