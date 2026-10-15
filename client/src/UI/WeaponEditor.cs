using System.Numerics;
using RimFps.Game;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// The weapon part of the HUD's edit mode: the weapon in hand held still, at the hip or aimed,
/// to be moved about with the mouse. Drag it to move it, right-drag to turn it, wheel to size
/// it (Shift + wheel rolls it, Ctrl + wheel thickens it); drag either hand, or the muzzle,
/// along the weapon. A panel beside it has the same as sliders, the number of hands, and resets.
/// Each weapon keeps its own pose (by its RimWorld picture), and can hand it to every weapon of
/// its kind.
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
        new("Turn", (p, a) => a ? p.AimYaw : p.Yaw, (p, a, v) => { if (a) p.AimYaw = v; else p.Yaw = v; }, -80, 80, "0"),
        new("Tilt", (p, a) => a ? p.AimPitch : p.Pitch, (p, a, v) => { if (a) p.AimPitch = v; else p.Pitch = v; }, -45, 100, "0"),
        new("Roll", (p, a) => a ? p.AimRoll : p.Roll, (p, a, v) => { if (a) p.AimRoll = v; else p.Roll = v; }, -90, 90, "0"),
        new("Left / right", (p, a) => a ? p.AimX : p.X, (p, a, v) => { if (a) p.AimX = v; else p.X = v; }, -0.3f, 0.3f, "0.000"),
        new("Up / down", (p, a) => a ? p.AimY : p.Y, (p, a, v) => { if (a) p.AimY = v; else p.Y = v; }, -0.3f, 0.15f, "0.000"),
        new("Distance", (p, a) => -(a ? p.AimZ : p.Z), (p, a, v) => { if (a) p.AimZ = -v; else p.Z = -v; }, 0.15f, 0.7f, "0.00"),
        new("Size", (p, _) => p.Size, (p, _, v) => p.Size = v, 0.3f, 3f, "0.00"),
        new("Thickness", (p, _) => p.Depth, (p, _, v) => p.Depth = v, 0f, 4f, "0.0"),
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

    private static Vector2 Screen(Vector3 view) => Raylib.GetWorldToScreen(view, Hands.Camera);

    /// <summary>The weapon's outline on the screen.</summary>
    private static Vector2[] Outline(Hands hands)
    {
        WeaponModel m = hands.Shown!;
        Matrix4x4 pose = hands.ShownPose;
        return new[]
        {
            Screen(Vector3.Transform(new Vector3(0, 0, 0), pose)), Screen(Vector3.Transform(new Vector3(m.Length, 0, 0), pose)),
            Screen(Vector3.Transform(new Vector3(m.Length, m.Height, 0), pose)), Screen(Vector3.Transform(new Vector3(0, m.Height, 0), pose)),
        };
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

    /// <summary>Where the mouse falls on the weapon's flat side, in its model space.</summary>
    private static Vector2? OnWeapon(Hands hands, Vector2 mouse)
    {
        Matrix4x4 pose = hands.ShownPose;
        Ray ray = Raylib.GetScreenToWorldRay(mouse, Hands.Camera);
        Vector3 origin = Vector3.Transform(Vector3.Zero, pose);
        Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, pose));
        float facing = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(facing) < 1e-4f) return null;
        float t = Vector3.Dot(origin - ray.Position, normal) / facing;
        if (t <= 0 || !Matrix4x4.Invert(pose, out Matrix4x4 inverse)) return null;
        Vector3 local = Vector3.Transform(ray.Position + ray.Direction * t, inverse);
        return new Vector2(local.X, local.Y);
    }

    private (Rectangle box, int index)[] SliderBoxes(float x, float y, float width, float line)
    {
        var boxes = new (Rectangle, int)[Sliders.Length];
        for (int i = 0; i < Sliders.Length; i++)
            boxes[i] = (new Rectangle(x + width * 0.42f, y + i * line + line * 0.3f, width * 0.4f, line * 0.4f), i);
        return boxes;
    }

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

        // Hip and aim tabs (guns only).
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
        var boxes = SliderBoxes(x, y, width, line);
        for (int i = 0; i < Sliders.Length; i++)
        {
            Slider s = Sliders[i];
            var (box, _) = boxes[i];
            float v = s.Get(shown, AimTab);
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
        }
        if (grab == Grab.Slider && downL)
        {
            Slider s = Sliders[slider];
            var (box, _) = boxes[slider];
            float share = Math.Clamp((mouse.X - box.X) / box.Width, 0, 1);
            s.Set(Own(settings, hands), AimTab, s.Min + (s.Max - s.Min) * share);
        }
        y += line * Sliders.Length + Ui.S(8);

        // Hands and resets.
        float third = (width - Ui.S(16)) / 3;
        string count = shown.HandCount switch { 1 => "One hand", 2 => "Two hands", _ => "Hands: auto" };
        if (Button(new Rectangle(x, y, third, Ui.S(28)), count, false))
            Own(settings, hands).HandCount = (shown.HandCount + 1) % 3;
        if (Button(new Rectangle(x + third + Ui.S(8), y, third, Ui.S(28)), "Auto hands", false))
        {
            WeaponPose p = Own(settings, hands);
            p.GripU = p.GripV = p.ForeU = p.ForeV = p.MuzzleU = p.MuzzleV = -1;
        }
        string kind = melee ? "*melee" : "*gun";
        // Reset: this weapon back to the pose of all its kind; pressed again, that back to the start.
        if (Button(new Rectangle(x + (third + Ui.S(8)) * 2, y, third, Ui.S(28)), "Reset", false))
            if (!settings.Weapons.Remove(hands.ShownName)) settings.Weapons.Remove(kind);
        y += Ui.S(36);
        if (Button(new Rectangle(x, y, width, Ui.S(28)), $"Use this pose for all {(melee ? "melee weapons" : "guns")}", false))
        {
            WeaponPose all = shown.Copy();
            settings.Weapons[kind] = all;
            // Weapons without their own pose follow; this one goes back to following too.
            settings.Weapons.Remove(hands.ShownName);
        }
        y += Ui.S(38);
        Ui.Text("Drag: move   Right-drag: turn   Wheel: size", x, y, Ui.S(14), Ui.Dim);
        Ui.Text("Shift+wheel: roll   Ctrl+wheel: thickness", x, y + Ui.S(18), Ui.S(14), Ui.Dim);
        Ui.Text("Drag the hands and the muzzle (orange)", x, y + Ui.S(36), Ui.S(14), Ui.Dim);

        // The weapon itself: its outline, its handles.
        Vector2[] outline = Outline(hands);
        for (int i = 0; i < 4; i++) Raylib.DrawLineEx(outline[i], outline[(i + 1) % 4], Math.Max(1f, Ui.S(1.5f)), new Color(232, 196, 86, 150));
        var handles = new List<(Grab what, Vector2 at, Color color)> { (Grab.Grip, Screen(hands.ShownGrip), Ui.Gold) };
        if (hands.ShownFore is Vector3 fore) handles.Add((Grab.Fore, Screen(fore), Ui.Gold));
        if (hands.ShownMuzzle is Vector3 muzzle) handles.Add((Grab.Muzzle, Screen(muzzle), Ui.Orange));
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
            {
                pose = Own(settings, hands);
                float z = MathF.Abs(AimTab ? pose.AimZ : pose.Z);
                float unit = 2f * z * MathF.Tan(Hands.Camera.FovY / 2f * MathF.PI / 180f) / Math.Max(1, h);
                if (AimTab)
                {
                    pose.AimX += delta.X * unit;
                    pose.AimY -= delta.Y * unit;
                }
                else
                {
                    pose.X += delta.X * unit;
                    pose.Y -= delta.Y * unit;
                }
                break;
            }
            case Grab.Turn when downR:
                pose = Own(settings, hands);
                if (AimTab)
                {
                    pose.AimYaw = Math.Clamp(pose.AimYaw - delta.X * 0.3f, -80, 80);
                    pose.AimPitch = Math.Clamp(pose.AimPitch - delta.Y * 0.3f, -45, 100);
                }
                else
                {
                    pose.Yaw = Math.Clamp(pose.Yaw - delta.X * 0.3f, -80, 80);
                    pose.Pitch = Math.Clamp(pose.Pitch - delta.Y * 0.3f, -45, 100);
                }
                break;
            case Grab.Grip or Grab.Fore or Grab.Muzzle when downL:
            {
                if (OnWeapon(hands, mouse) is not Vector2 at) break;
                pose = Own(settings, hands);
                float u = Math.Clamp(at.X / model.Length, -0.3f, 1.3f), v = Math.Clamp(at.Y / model.Height, -0.5f, 1.5f);
                if (grab == Grab.Grip)
                {
                    // The weapon is placed by its grip: moving the grip on it moves the place
                    // with it, so the weapon stays where it is and only the hand slides.
                    Vector3 now = Vector3.Transform(new Vector3(u * model.Length, v * model.Height, 0), hands.ShownPose);
                    pose.GripU = u;
                    pose.GripV = v;
                    if (AimTab) (pose.AimX, pose.AimY, pose.AimZ) = (now.X, now.Y, now.Z);
                    else (pose.X, pose.Y, pose.Z) = (now.X, now.Y, now.Z);
                }
                else if (grab == Grab.Fore)
                {
                    pose.ForeU = u;
                    pose.ForeV = v;
                }
                else
                {
                    pose.MuzzleU = u;
                    pose.MuzzleV = v;
                }
                break;
            }
        }

        // The wheel on the weapon.
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && free && (overWeapon || overHandle != Grab.None))
        {
            pose = Own(settings, hands);
            bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (shift)
            {
                if (AimTab) pose.AimRoll = Math.Clamp(pose.AimRoll + wheel * 3f, -90, 90);
                else pose.Roll = Math.Clamp(pose.Roll + wheel * 3f, -90, 90);
            }
            else if (ctrl) pose.Depth = Math.Clamp(pose.Depth + wheel * 0.2f, 0, 4);
            else pose.Size = Math.Clamp(pose.Size * MathF.Pow(1.06f, wheel), 0.3f, 3f);
        }
        Captured = overPanel || grab != Grab.None || free && (overWeapon || overHandle != Grab.None);
    }
}
