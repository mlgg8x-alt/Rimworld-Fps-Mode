using System.Numerics;
using Raylib_cs;

namespace RimFps.UI;

/// <summary>
/// Escape: Resume, Options, Edit HUD, Quit. Options change the look and the feel on the spot and
/// are saved when the menu closes. Arrow keys and Enter, or the mouse.
/// </summary>
public sealed class Menu
{
    public bool Open;
    public bool Quit;
    /// <summary>Edit HUD was chosen: the HUD's edit mode should start.</summary>
    public bool EditHud;
    /// <summary>The look changed in a way that needs every picture loaded again.</summary>
    public bool StyleChanged;
    private bool options;
    private int selected;
    private readonly Settings settings;

    public Menu(Settings settings) => this.settings = settings;

    public void Toggle()
    {
        if (Open && options)
        {
            options = false;
            selected = 1;
            return;
        }
        Open = !Open;
        options = false;
        selected = 0;
        if (!Open) settings.Save();
    }

    public void ShowOptions()
    {
        Open = true;
        options = true;
        selected = 0;
    }

    private string On(bool v) => v ? "On" : "Off";

    private List<(string label, Action<int>? change)> Items()
    {
        if (!options)
            return new() { ("Resume", null), ("Options", null), ("Edit HUD", null), ("Quit", null) };
        var items = new List<(string, Action<int>?)>
        {
            ($"Style: {(settings.Retro ? "Retro (90s shooter)" : "RimWorld")}", _ =>
            {
                settings.Style = settings.Retro ? "rimworld" : "retro";
                StyleChanged = true;
            }),
            ($"Mouse speed: {settings.Sensitivity * 100:0}", d => settings.Sensitivity = Math.Clamp(settings.Sensitivity + d * 0.01f, 0.02f, 0.6f)),
            ($"Aiming mouse speed: {settings.AdsSensitivity * 100:0}%", d => settings.AdsSensitivity = Math.Clamp(settings.AdsSensitivity + d * 0.05f, 0.1f, 2f)),
            ($"Invert mouse: {On(settings.InvertY)}", _ => settings.InvertY = !settings.InvertY),
            ($"Hold to aim: {On(settings.HoldToAim)}", _ => settings.HoldToAim = !settings.HoldToAim),
            ($"Field of view: {settings.Fov:0}", d => settings.Fov = Math.Clamp(settings.Fov + d * 5, 50, 110)),
            ($"Volume: {settings.Volume * 100:0}%", d => settings.Volume = Math.Clamp(settings.Volume + d * 0.1f, 0, 1)),
            ($"Weapon bob: {On(settings.Bob)}", _ => settings.Bob = !settings.Bob),
            ($"Fullscreen when playing: {On(settings.Fullscreen)}", _ => settings.Fullscreen = !settings.Fullscreen),
            ($"Show FPS: {On(settings.ShowFps)}", _ => settings.ShowFps = !settings.ShowFps),
        };
        if (settings.Retro)
        {
            items.Add(($"Retro lines: {settings.RenderHeight}", d => settings.RenderHeight = Step(settings.RenderHeight, d, new[] { 160, 180, 200, 240, 270, 360, 480 })));
            items.Add(($"Retro palette: {settings.Palette}", d => settings.Palette = Step(settings.Palette, d, new[] { "ramps", "ramps64", "off" })));
            items.Add(($"Retro dither: {On(settings.Dither)}", _ => settings.Dither = !settings.Dither));
            items.Add(($"Retro dark distance: {On(settings.Diminish)}", _ => settings.Diminish = !settings.Diminish));
        }
        items.Add(("Back", null));
        return items;
    }

    private static T Step<T>(T now, int d, T[] values)
    {
        int i = Array.IndexOf(values, now);
        if (i < 0) i = 0;
        return values[((i + (d == 0 ? 1 : d)) % values.Length + values.Length) % values.Length];
    }

    public void Draw(int w, int h, Vector2 mouse, bool click)
    {
        if (!Open) return;
        Raylib.DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 140));
        var items = Items();
        float size = Ui.S(options ? 20 : 28), gap = Ui.S(options ? 30 : 44);
        float pw = Ui.S(options ? 560 : 360), ph = gap * items.Count + Ui.S(110);
        var panel = new Rectangle((w - pw) / 2, (h - ph) / 2, pw, ph);
        Ui.Panel(panel, new Color(22, 23, 25, 235));
        Ui.Centred(options ? "Options" : "RimWorld FPS", w / 2f, panel.Y + Ui.S(18), Ui.S(34), Ui.White);
        float top = panel.Y + Ui.S(80);
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) selected = (selected + 1) % items.Count;
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) selected = (selected + items.Count - 1) % items.Count;
        int change = Raylib.IsKeyPressed(KeyboardKey.Right) ? 1 : Raylib.IsKeyPressed(KeyboardKey.Left) ? -1 : 0;
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
        selected = Math.Clamp(selected, 0, items.Count - 1);
        for (int i = 0; i < items.Count; i++)
        {
            float y = top + i * gap;
            var box = new Rectangle(panel.X + Ui.S(16), y - Ui.S(4), panel.Width - Ui.S(32), gap - Ui.S(4));
            if (Raylib.CheckCollisionPointRec(mouse, box) && Raylib.GetMouseDelta() != Vector2.Zero) selected = i;
            if (i == selected) Raylib.DrawRectangleRec(box, new Color(70, 72, 76, 160));
            Ui.Centred(items[i].label, w / 2f, y, size, i == selected ? Ui.Gold : Ui.White);
            if (click && Raylib.CheckCollisionPointRec(mouse, box))
            {
                selected = i;
                enter = true;
            }
        }
        if (options) Ui.Centred("Left / right to change, Enter or click to switch", w / 2f, panel.Y + panel.Height - Ui.S(28), Ui.S(15), Ui.Dim);
        var (_, act) = items[selected];
        if (change != 0) act?.Invoke(change);
        if (!enter) return;
        if (options)
        {
            if (act == null)
            {
                options = false;
                selected = 1;
                settings.Clamp();
            }
            else act(0);
            return;
        }
        switch (selected)
        {
            case 0:
                Toggle();
                break;
            case 1:
                options = true;
                selected = 0;
                break;
            case 2:
                Toggle();
                EditHud = true;
                break;
            case 3:
                settings.Save();
                Quit = true;
                break;
        }
    }
}
