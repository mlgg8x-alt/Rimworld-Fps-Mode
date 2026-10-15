using System.Globalization;

namespace RimFps;

public static class Program
{
    private const string Help = @"RimFpsClient: RimWorld FPS Mode's first-person client.

The RimWorld FPS Mode mod starts it in the background with RimWorld; or start it, then load a
save with the mod and turn on ""FPS mode"" on a colonist. Settings live in " + "{0}" + @".

  --demo              a made-up raid on a made-up colony, no RimWorld needed
  --address host:port where to listen for RimWorld (default 127.0.0.1:47820)
  --windowed          do not fill the screen when a colonist is handed over
  --keep-window       keep the window up while no colonist is played
  --size WxH          the window's size at start (default 1280x720)
  --background        as the mod starts it: hidden until a colonist is played, logging to
                      client.log beside the settings
  --parent PID        close when this process (RimWorld) does

For testing (with --demo): --screenshot file.png [--frames N] [--yaw D] [--pitch D]
  [--at x,z] [--sky 0..1] [--rain R] [--fog F] [--burning] [--menu] [--options] [--automap]
  [--loot] [--edit] [--aim] [--fire] [--swing] [--weapon Gun_Revolver|MeleeWeapon_LongSword|...]";

    public static int Main(string[] args)
    {
        var launch = new Launch();
        Settings settings = Settings.Load();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : "";
            float Num() => float.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;
            switch (a)
            {
                case "--demo": launch.Demo = true; break;
                case "--address": settings.Address = Next(); break;
                case "--windowed": settings.Fullscreen = false; break;
                case "--keep-window": settings.OneWindow = false; break;
                case "--background": launch.Background = true; settings.OneWindow = true; break;
                case "--parent": launch.Parent = (int)Num(); break;
                case "--touch": launch.Touch = true; break;
                case "--screenshot": launch.Screenshot = Path.GetFullPath(Next()); break;
                case "--frames": launch.Frames = (int)Num(); break;
                case "--yaw": launch.Yaw = Num(); break;
                case "--pitch": launch.Pitch = Num(); break;
                case "--sky": launch.Sky = Num(); break;
                case "--rain": launch.Rain = Num(); break;
                case "--fog": launch.Fog = Num(); break;
                case "--burning": launch.Burning = true; break;
                case "--fire": launch.Fire = true; break;
                case "--aim": launch.Aim = true; break;
                case "--swing": launch.Swing = true; break;
                case "--edit": launch.Edit = true; break;
                case "--weapon": launch.Weapon = Next(); break;
                case "--menu": launch.Menu = true; break;
                case "--options": launch.Menu = launch.Options = true; break;
                case "--automap": launch.Automap = true; break;
                case "--loot": launch.Loot = true; break;
                case "--at":
                    launch.Position = Next().Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    break;
                case "--size":
                    var wh = Next().Split('x');
                    if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h))
                    {
                        launch.Width = w;
                        launch.Height = h;
                    }
                    break;
                case "-h" or "--help" or "/?":
                    Console.WriteLine(string.Format(Help, Settings.Path));
                    return 0;
                default:
                    Console.WriteLine($"unknown option {a}; --help lists them");
                    return 2;
            }
        }
        if (Platform.Native.Android && settings.DemoOnStart) launch.Demo = true;
        if (launch.Background)
        {
            // No console to write to: a log beside the settings, fresh each start.
            try
            {
                string log = Path.Combine(Path.GetDirectoryName(Settings.Path)!, "client.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                var writer = new StreamWriter(log, false) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            Console.WriteLine($"[fps] started by RimWorld (process {launch.Parent}) at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        if (launch.Screenshot != null)
        {
            // A test shot: the same look every time, whatever the saved settings say, but with
            // the saved weapon poses; and nothing saved after.
            settings = new Settings { Weapons = settings.Weapons, Scratch = true };
            settings.Clamp();
        }
        using var app = new App(settings, launch);
        app.Run();
        return 0;
    }
}
