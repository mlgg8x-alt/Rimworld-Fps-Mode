using System.Numerics;
using RimFps.Audio;
using RimFps.Game;
using RimFps.Net;
using RimFps.Platform;
using RimFps.Render;
using RimFps.UI;
using RimFps.World;
using Raylib_cs;

namespace RimFps;

/// <summary>Options from the command line.</summary>
public sealed class Launch
{
    public bool Demo;
    public string? Screenshot;
    public int Frames = 90;
    public float? Yaw, Pitch, Sky;
    public float[]? Position;
    public bool Menu, Automap, Loot, Options, Edit, Aim, Fire, Swing;
    public int Width = 1280, Height = 720;
    public float Rain, Fog;
    public bool Burning;
    public string? Weapon;
    /// <summary>Started by the mod with RimWorld: hidden until a colonist is played, and gone
    /// when RimWorld (process <see cref="Parent"/>) is.</summary>
    public bool Background;
    public int Parent;
}

/// <summary>
/// The client: one window, one loop. Each frame it takes what RimWorld said, moves the player
/// and their weapon, flies RimWorld's bullets and shells, draws the world, the weapon in hand
/// and the HUD, and tells RimWorld what the player did.
/// </summary>
public sealed class App : IDisposable
{
    private readonly Settings settings;
    private readonly Launch launch;
    private readonly Bridge? bridge;
    private readonly Demo.FakeRimWorld? demo;
    private readonly Link link = new();
    private Atlas atlas;
    private Pictures pictures;
    private Builtins art;
    private readonly Grid grid = new();
    private WorldMesh mesh;
    private readonly Renderer renderer;
    private readonly Sprites sprites = new();
    private readonly Player player = new();
    private readonly Figures figures = new();
    private readonly Gun gun = new();
    private readonly Hands hands = new();
    private readonly WeaponEditor weaponEditor = new();
    private readonly Projectiles projectiles = new();
    private readonly Effects effects = new();
    private readonly Scenery scenery = new();
    private readonly Hud hud;
    private readonly LootPanel loot = new();
    private readonly Menu menu;
    private readonly Automap automap = new();
    private readonly Sfx sfx;

    private long ownWindow;
    private int connections;
    private bool told, hidden, captured, fullscreen, menuTold, toggledAim;
    private float rimworldGone, sinceMinimap, parentCheck;
    private bool started;

    private static bool Alive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
    private string? minimapSent;
    private int frame;
    private bool built;
    private string? lookingAt, prompt;
    private readonly Dictionary<string, bool> wasDown = new();
    private readonly Random random = new();

    public App(Settings settings, Launch launch)
    {
        this.settings = settings;
        this.launch = launch;
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        var flags = ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint;
        if (!settings.Retro) flags |= ConfigFlags.Msaa4xHint;
        if (launch.Background) flags |= ConfigFlags.HiddenWindow;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(launch.Width, launch.Height, "RimWorld FPS");
        Raylib.SetWindowMinSize(480, 300);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(launch.Screenshot != null ? 0 : 165);
        unsafe
        {
            ownWindow = Native.Windows ? (long)Raylib.GetWindowHandle() : 0;
        }
        Ui.Load();
        renderer = new Renderer(settings);
        atlas = new Atlas(smooth: !settings.Retro);
        pictures = new Pictures(atlas, settings);
        art = new Builtins(pictures, settings);
        mesh = new WorldMesh(grid, pictures, art);
        renderer.UseAtlas(atlas);
        menu = new Menu(settings);
        hud = new Hud(settings);
        sfx = new Sfx { Volume = settings.Volume };
        if (launch.Demo)
        {
            demo = new Demo.FakeRimWorld(launch);
            link.Connected = true;
            hud.Message("Demo: a made-up raid on a made-up colony", Ui.Gold, 6f);
        }
        else
        {
            bridge = new Bridge(settings.Address) { OwnWindow = () => ownWindow.ToString() };
            Console.WriteLine($"[fps] waiting for RimWorld on {settings.Address}: load a save with the RimWorld FPS mod");
        }
        if (launch.Menu) menu.Toggle();
        if (launch.Options) menu.ShowOptions();
        if (launch.Automap) automap.Open = true;
        if (launch.Loot) loot.Open = true;
        if (launch.Edit) hud.Editing = true;
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose() && !menu.Quit)
        {
            Frame();
            if (launch.Screenshot != null && frame >= launch.Frames)
            {
                // raylib's own screenshot puts its folder in front of the path: the screen's
                // pixels straight to the file instead.
                Image shot = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(shot, launch.Screenshot);
                Raylib.UnloadImage(shot);
                Console.WriteLine($"[fps] screenshot {launch.Screenshot}");
                break;
            }
        }
    }

    /// <summary>A key pressed this frame, or held for a test shot.</summary>
    private bool Pressed(KeyboardKey key) => Raylib.IsKeyPressed(key);

    private void Frame()
    {
        frame++;
        float dt = Math.Clamp(Raylib.GetFrameTime(), 0.001f, 0.05f);
        if (launch.Screenshot != null) dt = 1f / 60f;
        Ui.Begin(Raylib.GetScreenHeight());
        Network();
        demo?.Update(dt, link, player);
        pictures.Sync(link);
        grid.Update(link);
        if (menu.StyleChanged)
        {
            menu.StyleChanged = false;
            Reload();
            hud.Message(settings.Retro ? "Retro style (restart for sharper edges in RimWorld style)" : "RimWorld style (restart for smoothed edges)", Ui.Grey);
        }
        if (grid.Ready)
        {
            mesh.Update(link, built ? 8 : int.MaxValue);
            built = true;
        }
        pictures.Pump(4.0);
        atlas.Refresh(dt);
        Windows(dt);

        // Input.
        bool focused = Raylib.IsWindowFocused() || launch.Screenshot != null;
        if (Pressed(KeyboardKey.Escape))
        {
            if (hud.Editing) StopEditing();
            else if (loot.Open) loot.Open = false;
            else if (automap.Open) automap.Open = false;
            else menu.Toggle();
        }
        if (menu.EditHud)
        {
            menu.EditHud = false;
            hud.Editing = true;
        }
        if (Pressed(KeyboardKey.F2))
        {
            if (hud.Editing) StopEditing();
            else hud.Editing = true;
        }
        bool frozen = menu.Open || loot.Open || hud.Editing;
        if (!frozen)
        {
            if (Pressed(KeyboardKey.F) && link.Player != null) loot.Toggle();
            if (Pressed(KeyboardKey.M)) automap.Open = !automap.Open;
            if (Pressed(KeyboardKey.Tab)) TabToRimWorld();
            if (Pressed(KeyboardKey.E) && link.Player is PawnState usePawn) Send(Out.Use(usePawn.Id));
            if (Pressed(KeyboardKey.F3)) settings.ShowFps = !settings.ShowFps;
        }
        frozen = menu.Open || loot.Open || hud.Editing;
        bool wantCapture = focused && !frozen && !hidden && link.Connected;
        if (wantCapture != captured && launch.Screenshot == null)
        {
            captured = wantCapture;
            if (captured) Raylib.DisableCursor();
            else Raylib.EnableCursor();
        }
        bool live = (captured || launch.Screenshot != null) && !frozen;
        Controls c = default;
        Vector2 look = Vector2.Zero;
        bool aiming = false, trigger = false, pulled = false;
        if (live)
        {
            if (captured) look = Raylib.GetMouseDelta();
            c.LookX = look.X;
            c.LookY = look.Y;
            c.Forward = (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0);
            c.Right = (Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0);
            bool space = Raylib.IsKeyDown(KeyboardKey.Space), crouchKey = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.C);
            c.Up = (space ? 1 : 0) - (crouchKey ? 1 : 0);
            c.Jump = Pressed(KeyboardKey.Space);
            c.Crouch = crouchKey;
            c.Sprint = Raylib.IsKeyDown(KeyboardKey.LeftShift);
            c.Walk = Raylib.IsKeyDown(KeyboardKey.LeftAlt);
            if (settings.HoldToAim) aiming = Raylib.IsMouseButtonDown(MouseButton.Right);
            else
            {
                if (Raylib.IsMouseButtonPressed(MouseButton.Right)) toggledAim = !toggledAim;
                aiming = toggledAim;
            }
            trigger = Raylib.IsMouseButtonDown(MouseButton.Left);
            pulled = Raylib.IsMouseButtonPressed(MouseButton.Left);
            if (launch.Aim) aiming = true;
            if (launch.Fire && frame > launch.Frames - 3) trigger = pulled = true;
            // Firing or aiming stops a sprint.
            if (trigger || aiming) c.Sprint = false;
            c.Aim = aiming;
        }
        if (launch.Yaw.HasValue && frame <= 2) player.Yaw = launch.Yaw.Value;
        if (launch.Pitch.HasValue && frame <= 2) player.Pitch = launch.Pitch.Value;
        player.AimSlow = 1f / MathF.Max(1f, 1f + (gun.Zoom - 1f) * hands.Aim);
        player.Update(dt, c, link, grid, Send, settings, frozen);
        bool bash = live && (Pressed(KeyboardKey.V) || launch.Swing && frame == launch.Frames - 8);
        gun.Update(dt, trigger, pulled, live && Pressed(KeyboardKey.R), live && Pressed(KeyboardKey.G), bash, aiming,
            link, player, figures, grid, Send, frozen);
        hands.Update(dt, player, gun, aiming, look);
        Sounds();

        // What RimWorld fired and set off since last frame.
        Projectiles.Body? body = player.Playing != null ? new Projectiles.Body(player.X, player.Z, player.EyeY, player.JumpY) : null;
        foreach (Shot shot in link.Shots) projectiles.Add(shot, grid, body, player.Playing, effects);
        link.Shots.Clear();
        foreach (Shell shell in link.Shells) projectiles.Add(shell);
        link.Shells.Clear();
        foreach (Explosion e in link.Explosions)
        {
            effects.Explosion(e, player.Eye);
            projectiles.Exploded(e);
            float distance = Vector2.Distance(new Vector2(e.X, e.Z), new Vector2(player.X, player.Z));
            sfx.Play("boom", Math.Clamp(1.2f - distance / 50f, 0.15f, 1f), e.Damage.Contains("EMP") ? 1.4f : 1f);
        }
        link.Explosions.Clear();
        foreach (FleckState f in link.Flecks)
            if (pictures.Get(f.Tex) is Pic pic) effects.Fleck(f, pic);
        link.Flecks.Clear();
        float gameDt = link.Paused ? 0 : dt * link.Rate;
        projectiles.Update(dt, body, player.Playing, effects);
        foreach (var (id, part, shooter, from) in projectiles.Struck)
        {
            if (player.Playing != null) Send(Out.PlayerStruck(player.Playing, id, part));
            hud.Hurt(part == "head" ? 1.3f : 0.8f);
            hud.DamageFrom(figures.All.TryGetValue(shooter, out Figure? who) ? who.X : from.X, who?.Z ?? from.Z, player);
            sfx.Play("hurt", 1f);
            effects.Shake = Math.Max(effects.Shake, 0.25f);
        }
        foreach (Vector3 _ in projectiles.Whistles) sfx.Play("whistle", 0.8f);
        if (projectiles.NearMisses > 0)
        {
            sfx.Play("whiz", 0.5f);
            projectiles.NearMisses = 0;
        }
        foreach (var (from, to, hit) in gun.Fired)
        {
            effects.Puff(to, hit ? Effects.Kind.Blood : Effects.Kind.Grit, 0.18f, hit ? 4 : 3);
            if (!hit) effects.Puff(to, Effects.Kind.Spark, 0.08f, 2);
            if (hit) hud.Hit(gun.LastHitPart);
        }
        if (gun.Swung && gun.LastHitAt >= gun.Clock - 0.01f) hud.Hit("melee");
        Confirm();
        figures.Update(link.Snap, dt);
        effects.Update(dt, gameDt);
        hud.Update(dt, link);
        if (hud.Editing && !weaponEditor.Captured) hud.Edit(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        Prompt();
        Draw(dt);
    }

    /// <summary>A foe the player struck in the last moments has gone down: a kill marker.</summary>
    private void Confirm()
    {
        foreach (Figure f in figures.All.Values)
        {
            bool down = f.Pawn.Downed || f.Pawn.Dead;
            bool was = wasDown.TryGetValue(f.Id, out bool w) && w;
            if (down && !was && f.Id == gun.LastTarget && gun.Clock - gun.LastHitAt < 3f && f.Id != player.Playing)
            {
                hud.Kill(f.Pawn.Name ?? "them");
                sfx.Play("hit", 0.9f, 0.7f);
            }
            wasDown[f.Id] = down;
        }
    }

    /// <summary>What E would do now, for the HUD: work the door in front, pick up or put down
    /// someone downed.</summary>
    private void Prompt()
    {
        prompt = null;
        PawnState? pawn = link.Player;
        if (pawn == null || !grid.Ready) return;
        if (pawn.Carry is { Pawn: true })
        {
            prompt = "[E] Put down";
            return;
        }
        float yaw = player.Yaw * MathF.PI / 180f;
        var facing = new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        float best = float.MaxValue;
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = (int)player.X + dx, z = (int)player.Z + dz;
                if (grid.CodeAt(x, z) != Code.Door) continue;
                var to = new Vector2(x + 0.5f - player.X, z + 0.5f - player.Z);
                float d = to.Length();
                if (d > 1.6f || d < 0.4f || Vector2.Dot(to / d, facing) < 0.3f || d >= best) continue;
                best = d;
                prompt = grid.DoorSwing(x, z) >= 0.5f ? "[E] Close door" : "[E] Open door";
            }
        foreach (Figure f in figures.All.Values)
        {
            if (f.Id == player.Playing || !f.Pawn.Downed || f.Pawn.Dead) continue;
            var to = new Vector2(f.X - player.X, f.Z - player.Z);
            float d = to.Length();
            if (d > 1.6f || d >= best) continue;
            best = d;
            prompt = $"[E] Carry {f.Pawn.Name ?? "them"}";
        }
        if (prompt == null && link.Loot is Loot l && l.Pawn == pawn.Id && l.Nearby.Any(n => n.Items.Count > 0))
            prompt = "[F] Search";
    }

    private void StopEditing()
    {
        hud.Editing = false;
        hud.LayoutChanged = true;
        settings.Save();
    }

    private void Send(string line)
    {
        if (demo != null) demo.Receive(line, link);
        else bridge?.Send(line);
    }

    private void Network()
    {
        if (bridge == null) return;
        while (bridge.Status.TryDequeue(out string? status))
        {
            Console.WriteLine($"[fps] {status}");
            hud.Message(status, Ui.Grey, 4f);
        }
        if (bridge.Connections != connections)
        {
            connections = bridge.Connections;
            link.Reset();
            told = false;
            minimapSent = null;
        }
        link.Connected = bridge.Connected;
        int budget = 4000;
        while (budget-- > 0 && bridge.Inbound.TryDequeue(out string? line)) link.Apply(line);
    }

    /// <summary>The one-application flow (upstream's M12): the window hides while RimWorld
    /// plays nobody here, comes up (filling the screen) when "Play in first person" hands a
    /// colonist over, and closes once RimWorld has. Also keeps RimWorld's minimap window where
    /// the HUD's minimap widget is.</summary>
    private void Windows(float dt)
    {
        if (bridge == null) return;
        if (link.Connected && !told && ownWindow != 0)
        {
            bridge.SendHello();
            told = true;
        }
        bool hide = launch.Background
            ? !link.Connected || link.Snap?.PlayerPawn == null
            : settings.OneWindow && Native.Windows && link.Connected && link.Snapshots > 0 && link.Snap?.PlayerPawn == null;
        if (launch.Background && !started) hidden = true;
        started = true;
        if (hide != hidden)
        {
            hidden = hide;
            if (Native.Windows && ownWindow != 0) Native.Show(ownWindow, !hide);
            else if (hide) Raylib.SetWindowState(ConfigFlags.HiddenWindow);
            else Raylib.ClearWindowState(ConfigFlags.HiddenWindow);
            if (!hide && settings.Fullscreen && !fullscreen && !Raylib.IsWindowFullscreen())
            {
                Raylib.ToggleBorderlessWindowed();
                fullscreen = true;
                hud.LayoutChanged = true;
            }
            Console.WriteLine(hide ? "[fps] hiding until RimWorld hands over a colonist" : "[fps] playing a colonist");
        }
        if (launch.Parent > 0 && (parentCheck -= dt) <= 0)
        {
            parentCheck = 2f;
            if (!Alive(launch.Parent))
            {
                Console.WriteLine("[fps] RimWorld has closed: closing too");
                menu.Quit = true;
            }
        }
        if (settings.OneWindow && link.RimworldWindow != 0 && !link.Connected && !Native.Exists(link.RimworldWindow))
        {
            rimworldGone += dt;
            if (rimworldGone > 3f)
            {
                Console.WriteLine("[fps] RimWorld has closed: closing too");
                menu.Quit = true;
            }
        }
        else rimworldGone = 0;
        bool menuOpen = menu.Open;
        if (menuOpen != menuTold && link.Connected)
        {
            menuTold = menuOpen;
            Send(Out.Menu(menuOpen));
        }
        // The minimap widget's place, for RimWorld's window.
        sinceMinimap += dt;
        if (link.Connected && (hud.LayoutChanged || sinceMinimap > 3f || Raylib.IsWindowResized()))
        {
            var (r, shown, round, zoom) = hud.Minimap(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            string line = Out.Minimap(r.X, r.Y, r.Width, shown, round, zoom);
            if (line != minimapSent || sinceMinimap > 3f)
            {
                Send(line);
                minimapSent = line;
            }
            hud.LayoutChanged = false;
            sinceMinimap = 0;
        }
    }

    private void TabToRimWorld()
    {
        if (link.RimworldWindow == 0)
        {
            hud.Message(demo != null ? "Tab brings up RimWorld (not in the demo)" : "RimWorld did not say which window is its own", Ui.Grey);
            return;
        }
        // RimWorld hears first that the player asked for it, then comes up over us.
        Send(Out.RimworldShown(ownWindow.ToString()));
        if (!Native.BringUp(link.RimworldWindow, ownWindow)) hud.Message("Could not bring up RimWorld", Ui.Red);
        Raylib.EnableCursor();
        captured = false;
    }

    private void Sounds()
    {
        sfx.Volume = settings.Volume;
        if (gun.Fired.Count > 0) sfx.Play(Sfx.ShotFor(gun.WeaponDef), 0.9f);
        if (gun.Swung) sfx.Play("swing", 0.8f);
        if (gun.Thrown) sfx.Play("throw", 0.8f);
        if (gun.Kind == Gun.Mode.Gun && gun.Cycle > 0 && gun.Cycle + Raylib.GetFrameTime() >= gun.CycleLength) sfx.Play("reload", 0.5f);
    }

    /// <summary>The look changed: every picture again, sharp or smooth.</summary>
    private void Reload()
    {
        hands.Forget();
        mesh.Dispose();
        atlas.Dispose();
        atlas = new Atlas(smooth: !settings.Retro);
        pictures = new Pictures(atlas, settings);
        art = new Builtins(pictures, settings);
        mesh = new WorldMesh(grid, pictures, art);
        renderer.UseAtlas(atlas);
        renderer.ForgetMaterials();
        grid.ShapeDirty = true;
        built = false;
        pictures.Sync(link);
    }

    /// <summary>How lit a point is (RimWorld x, z, height y), as a sprite's colour.</summary>
    private Color Lit(float x, float z, float y, float sky, Vector3 skyTint, Vector3 lampTint)
    {
        int cx = (int)MathF.Floor(x), cz = (int)MathF.Floor(z);
        if (!grid.Ready) return Color.White;
        float lamp = grid.Lamp(cx, cz), outdoors = grid.Outdoors(cx, cz) * sky;
        Vector3 c = Vector3.Max(lampTint * lamp, skyTint * outdoors);
        c = Vector3.Max(c, new Vector3(0.07f));
        return Renderer.ToColor(c);
    }

    private void Draw(float dt)
    {
        int ww = Raylib.GetScreenWidth(), wh = Raylib.GetScreenHeight();
        Weather weather = link.Weather;
        if (launch.Rain > 0) weather = new Weather { Rain = launch.Rain, Sky = weather.Sky, Fog = launch.Fog, Wind = 1 };
        else if (launch.Fog > 0) weather = new Weather { Fog = launch.Fog, Sky = weather.Sky };
        float daylight = Math.Clamp(launch.Sky ?? weather.Sky, 0f, 1f);
        float murk = Math.Clamp(weather.Fog + weather.Rain * 0.35f + weather.Snow * 0.3f, 0f, 1f);
        float skyLight = 0.18f + 0.82f * daylight * (1f - murk * 0.25f);
        Vector3 skyTint = Vector3.Lerp(new Vector3(0.55f, 0.62f, 1f), new Vector3(1f, 0.98f, 0.94f), daylight);
        var lampTint = new Vector3(1.15f, 1.05f, 0.9f);
        Vector3 fogColor = Vector3.Lerp(new Vector3(0.04f, 0.05f, 0.09f), new Vector3(0.72f, 0.79f, 0.85f), daylight);
        fogColor = Vector3.Lerp(fogColor, new Vector3(0.5f, 0.52f, 0.55f) * (0.2f + 0.8f * daylight), murk);
        float reach = settings.ViewDistance * (1f - weather.Fog * 0.78f) * (1f - Math.Clamp(weather.Rain, 0, 1) * 0.3f);
        renderer.Light(new Lighting
        {
            Sky = skyLight,
            SkyTint = skyTint,
            LampTint = lampTint,
            Ambient = 0.06f,
            FogColor = fogColor,
            FogStart = reach * (settings.Retro ? 0.3f : 0.45f),
            FogEnd = reach,
        });
        Color LitAt(float x, float z, float y) => Lit(x, z, y, skyLight, skyTint, lampTint);

        Vector3 eye = player.Eye;
        if (effects.Shake > 0)
            eye += new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * effects.Shake * 0.1f;
        Vector3 forward = player.Forward;
        float zoom = 1f + (gun.Zoom - 1f) * hands.Aim;
        float fov = 2f * MathF.Atan(MathF.Tan(settings.Fov * MathF.PI / 360f) / zoom) * 180f / MathF.PI;
        var camera = new Camera3D(eye, eye + forward, Vector3.UnitY, fov, CameraProjection.Perspective);
        sprites.Right = player.Right;

        Raylib.BeginDrawing();
        renderer.BeginScene(ww, wh);
        renderer.DrawSky(player.ViewYaw, player.ViewPitch, fov, skyLight, murk, fogColor);
        Raylib.BeginMode3D(camera);
        Rlgl.DisableBackfaceCulling();
        if (grid.Ready)
        {
            mesh.Draw(renderer.Material, new Vector2(player.X, player.Z), new Vector2(forward.X, -forward.Z), forward.Y, reach);
            scenery.Draw(sprites, link, grid, pictures, art, eye, skyLight, LitAt);
            Effects.Filth(sprites, pictures, link.Filth, eye, grid, LitAt);
        }
        figures.Draw(sprites, pictures, grid, eye, player.Playing, skyLight, LitAt);
        projectiles.Draw(sprites, art, pictures, eye, LitAt);
        effects.Fires(sprites, art, link.Fires, eye);
        effects.Gas(sprites, art, link.Gas, LitAt);
        effects.Draw(sprites, art, LitAt);
        if (grid.Ready) effects.Weather(sprites, art, weather, eye, grid, daylight);
        int spriteCount = sprites.Count;
        sprites.Flush(renderer, atlas);
        Rlgl.EnableBackfaceCulling();
        Raylib.EndMode3D();
        renderer.EndScene();
        // The weapon in hand, flat on the screen in front of everything, lit where the player stands.
        if (!automap.Open)
        {
            Color here = LitAt(player.X, player.Z, 1f);
            hands.Hold = hud.Editing ? (weaponEditor.AimTab ? 1f : 0f) : null;
            hands.Draw(link, player, gun, pictures, atlas, art, settings, new Vector3(here.R, here.G, here.B) / 255f * 1.05f, ww, wh);
        }

        // Who the crosshair is on.
        lookingAt = null;
        if (player.Playing != null)
        {
            Vector3 from = new(eye.X, eye.Y, -eye.Z), way = new(forward.X, forward.Y, -forward.Z);
            var (_, _, target) = Gun.Trace(from, way, 45f, grid, figures);
            if (target is Target t && figures.All.TryGetValue(t.Id, out Figure? f))
                lookingAt = $"{f.Pawn.Name ?? f.Pawn.Kind}{(f.Pawn.Hostile ? " - hostile" : f.Pawn.Downed ? " - downed" : "")}";
        }

        Vector2 mouse = Raylib.GetMousePosition();
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        automap.Draw(ww, wh, grid, player, figures);
        hud.Draw(new HudFrame
        {
            W = ww,
            H = wh,
            Link = link,
            Gun = gun,
            Player = player,
            Atlas = atlas,
            Pictures = pictures,
            Figures = figures,
            Grid = grid,
            Aim = hands.Aim,
            LookingAt = lookingAt,
            Prompt = prompt,
            Overlay = link.Overlay,
            Mouse = mouse,
        });
        if (hud.Editing) weaponEditor.Draw(hands, settings, ww, wh, hud.Busy(mouse));
        if (!link.Connected) Ui.Centred($"Waiting for RimWorld on {settings.Address}", ww / 2f, wh / 2f - Ui.S(20), Ui.S(26), Ui.Gold);
        else if (!grid.Ready) Ui.Centred("Loading RimWorld's map...", ww / 2f, wh / 2f - Ui.S(20), Ui.S(26), Ui.Gold);
        else if (player.Flying && !hud.Editing)
            Ui.Centred("Spectating: select a colonist in RimWorld and turn on \"FPS mode\"", ww / 2f, wh - Ui.S(46), Ui.S(20), Ui.Grey);
        loot.Draw(ww, wh, link, mouse, click && loot.Open, Send, hud);
        menu.Draw(ww, wh, mouse, click && menu.Open);
        if (settings.ShowFps)
        {
            Ui.Text($"{Raylib.GetFPS()} fps  sprites {spriteCount}  pictures {pictures.Count} (+{pictures.Waiting})  atlas {atlas.Pages}", Ui.S(8), wh - Ui.S(52), Ui.S(16), Ui.Green);
            Ui.Text($"at {player.X:0.0},{player.Z:0.0}  yaw {player.Yaw:0}  chunks {mesh.ChunksBuilt}  effects {effects.Count}  shots {projectiles.Count}", Ui.S(8), wh - Ui.S(32), Ui.S(16), Ui.Green);
        }
        Raylib.EndDrawing();
    }

    public void Dispose()
    {
        settings.Save();
        bridge?.Dispose();
        sfx.Dispose();
        hands.Dispose();
        mesh.Dispose();
        atlas.Dispose();
        renderer.Dispose();
        Ui.Unload();
        Raylib.CloseWindow();
    }
}
