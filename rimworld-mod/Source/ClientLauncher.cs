using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// The FPS client comes with the mod (in <c>Client/</c>) and runs in the background from the
    /// moment RimWorld starts: hidden until a colonist is played in first person, started again
    /// if it stops, and closed with RimWorld. A client the player started themselves is left
    /// alone.
    /// </summary>
    public static class ClientLauncher
    {
        private static Process process;
        private static string exe;
        private static string modRoot;
        private static bool manualStart;
        private static float nextCheck;
        private static int restarts;
        private const int MaxRestarts = 5;

        /// <summary>The client's program for this computer, in the mod's folder; null when there is none.</summary>
        private static string Find(string root)
        {
            string dir = Path.Combine(root, "Client");
            string[] candidates = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? new[] { Path.Combine(dir, "win-x64", "RimFpsClient.exe"), Path.Combine(dir, "RimFpsClient.exe") }
                : Application.platform == RuntimePlatform.LinuxPlayer
                    ? new[] { Path.Combine(dir, "linux-x64", "RimFpsClient") }
                    : new string[0];
            foreach (string path in candidates)
                if (File.Exists(path)) return path;
            return null;
        }

        private static bool running;
        private static float runningAt = -10f;
        /// <summary>Why the client is not running, for the dot's tooltip; null when nothing went wrong.</summary>
        public static string Problem;

        public static bool FatalError => !string.IsNullOrEmpty(Problem);

        public static string StatusText => BridgeGameComponent.Linked
            ? "Online and connected"
            : FatalError
                ? "Fatal error: " + Problem
                : Running ? "Running, not connected" : "Not connected";

        /// <summary>Something listens on the bridge's port (our client, or one started by hand):
        /// tried by binding it, which needs no rights, unlike listing processes. Once a second.</summary>
        public static bool Running
        {
            get
            {
                if (Time.realtimeSinceStartup - runningAt < 1f) return running;
                runningAt = Time.realtimeSinceStartup;
                running = PortInUse() || process != null && !HasExited(process);
                return running;
            }
        }

        private static bool PortInUse()
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Loopback, BridgeMod.Settings.Port);
                probe.Start();
                return false;
            }
            catch (SocketException)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try { probe?.Stop(); } catch (Exception) { }
            }
        }

        private static bool HasExited(Process p)
        {
            try
            {
                return p.HasExited;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>At RimWorld's start.</summary>
        public static void Start(string modRoot)
        {
            ClientLauncher.modRoot = modRoot;
            try
            {
                exe = Find(modRoot);
            }
            catch (Exception e)
            {
                exe = null;
                Problem = "could not look for the client: " + e.Message;
            }
            if (exe == null)
            {
                Problem = Problem ?? $"no client for {Application.platform} in {Path.Combine(modRoot, "Client")}";
                Log.Error("[RimWorldFPS] " + Problem);
                return;
            }
            Log.Message($"[RimWorldFPS] FPS client: {exe}");
            Launch();
        }

        private static void Launch(bool force = false)
        {
            if ((!force && !BridgeMod.Settings.LaunchClient) || exe == null) return;
            if (PortInUse())
            {
                Log.Message($"[RimWorldFPS] something already listens on port {BridgeMod.Settings.Port}: the FPS client is running");
                Problem = null;
                return;
            }
            if (Application.platform != RuntimePlatform.WindowsPlayer) MakeRunnable(exe);
            try
            {
                int self = Process.GetCurrentProcess().Id;
                var info = new ProcessStartInfo(exe, $"--background --parent {self} --address {BridgeMod.Settings.Host}:{BridgeMod.Settings.Port}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                process = Process.Start(info);
                if (process == null)
                    throw new InvalidOperationException("The operating system did not return a client process.");
                runningAt = -10f;
                Problem = null;
                Log.Message($"[RimWorldFPS] started the FPS client in the background (process {process?.Id}); its log: {LogPath}");
            }
            catch (Exception e)
            {
                Problem = $"could not start {exe}: {e.Message}";
                Log.Error("[RimWorldFPS] " + Problem);
                process = null;
            }
        }

        /// <summary>Where the client writes its log (it starts it fresh each run).</summary>
        public static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorldFPS", "client.log");

        /// <summary>Linux and macOS: the program marked runnable (unzipping often drops that).</summary>
        private static void MakeRunnable(string path)
        {
            foreach (string chmod in new[] { "/bin/chmod", "/usr/bin/chmod", "chmod" })
            {
                try
                {
                    if (chmod.StartsWith("/") && !File.Exists(chmod)) continue;
                    using (Process p = Process.Start(new ProcessStartInfo(chmod, $"+x \"{path}\"") { UseShellExecute = false, CreateNoWindow = true }))
                        p?.WaitForExit(3000);
                    return;
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimWorldFPS] could not mark the client runnable with {chmod}: {e.Message}");
                }
            }
        }

        /// <summary>Every frame: started again if it stopped (a few times at most).</summary>
        public static void Watch()
        {
            if (exe == null || (!BridgeMod.Settings.LaunchClient && !manualStart) || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 5f;
            if (Running) return;
            if (restarts >= MaxRestarts)
            {
                Problem = Problem ?? $"the client keeps stopping; see its log: {LogPath}";
                return;
            }
            restarts++;
            Log.Message($"[RimWorldFPS] the FPS client stopped; starting it again ({restarts}/{MaxRestarts})");
            Launch(manualStart);
        }

        /// <summary>The dot was clicked while offline: try again now.</summary>
        public static void Restart()
        {
            ForceStart();
        }

        /// <summary>Starts the client even when automatic launch is disabled.</summary>
        public static void ForceStart()
        {
            manualStart = true;
            restarts = 0;
            nextCheck = 0;
            Problem = null;
            if (exe == null && !string.IsNullOrEmpty(modRoot))
            {
                try { exe = Find(modRoot); }
                catch (Exception e) { Problem = "could not look for the client: " + e.Message; }
            }
            if (exe == null)
            {
                Problem = Problem ?? $"no client for {Application.platform} in the mod's Client folder";
                Log.Error("[RimWorldFPS] " + Problem);
            }
            else Launch(true);
        }

        /// <summary>RimWorld is quitting: the client goes too.</summary>
        public static void Stop()
        {
            try
            {
                if (process != null && !HasExited(process)) process.Kill();
            }
            catch (Exception)
            {
            }
            process = null;
            manualStart = false;
        }
    }
}
