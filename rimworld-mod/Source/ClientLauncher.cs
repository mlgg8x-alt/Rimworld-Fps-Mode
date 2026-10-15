using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using RimWorld;
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
            if (exe == null || !force && !BridgeMod.Settings.LaunchClient) return;
            if (!force && PortInUse())
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
                runningAt = -10f;
                startedAt = Time.realtimeSinceStartup;
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
            if (exe == null || !BridgeMod.Settings.LaunchClient || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 5f;
            if (Running) return;
            if (restarts >= MaxRestarts)
            {
                Problem = Problem ?? $"the client keeps stopping; see its log: {LogPath}";
                return;
            }
            restarts++;
            Log.Message($"[RimWorldFPS] the FPS client stopped; starting it again ({restarts}/{MaxRestarts})");
            Launch();
        }

        /// <summary>The dot was clicked while offline: try again now.</summary>
        public static void Restart()
        {
            restarts = 0;
            nextCheck = 0;
            if (exe == null) Log.Error("[RimWorldFPS] " + (Problem ?? "no FPS client for this computer in the mod's Client folder"));
            else Launch();
        }

        /// <summary>How the client is, for the status indicator.</summary>
        public enum State { Online, Waiting, Starting, Offline }

        private static float startedAt = -100f;

        public static State Now(bool connected)
        {
            if (connected) return State.Online;
            if (Running) return State.Waiting;
            if (Time.realtimeSinceStartup - startedAt < 15f && process != null && !HasExited(process)) return State.Starting;
            return State.Offline;
        }

        public static string Label(State state) => state switch
        {
            State.Online => "FPS online",
            State.Waiting => "FPS ready",
            State.Starting => "FPS starting",
            _ => "FPS offline",
        };

        public static string Describe(State state, bool inGame) => state switch
        {
            State.Online => "Online: connected to the FPS client. Select a colonist and turn on \"FPS mode\".",
            State.Waiting => inGame
                ? "The FPS client is running; RimWorld is connecting to it."
                : "The FPS client is running. It connects when a game loads.",
            State.Starting => "The FPS client is starting.",
            _ => "Offline: the FPS client is not running." + (Problem != null ? "\nProblem: " + Problem : ""),
        } + "\nClient: " + (exe ?? "none found") + "\nClient log: " + LogPath;

        /// <summary>The force start button: whatever client runs is stopped (ours, or one left
        /// over, which may hold the port), and a fresh one started.</summary>
        public static void ForceStart()
        {
            Log.Message("[RimWorldFPS] force starting the FPS client");
            Stop();
            try
            {
                foreach (Process old in Process.GetProcessesByName("RimFpsClient"))
                {
                    try
                    {
                        old.Kill();
                        old.WaitForExit(2000);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[RimWorldFPS] could not stop an old FPS client (process {old.Id}): {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[RimWorldFPS] could not look for old FPS clients: " + e.Message);
            }
            restarts = 0;
            nextCheck = Time.realtimeSinceStartup + 5f;
            runningAt = -10f;
            Problem = null;
            if (exe == null)
            {
                Problem = "no client for this computer in the mod's Client folder";
                Log.Error("[RimWorldFPS] " + Problem);
                return;
            }
            Launch(force: true);
        }

        /// <summary>The client's log, opened in the computer's text viewer.</summary>
        public static void OpenLog()
        {
            if (File.Exists(LogPath)) Application.OpenURL("file://" + LogPath.Replace('\\', '/'));
            else Messages.Message("RimWorld FPS: no client log yet (" + LogPath + ")", MessageTypeDefOf.RejectInput, false);
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
        }
    }
}
