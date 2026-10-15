using System;
using System.Diagnostics;
using System.IO;
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

        /// <summary>The client is running (ours, or one started by hand); looked at once a second.</summary>
        public static bool Running
        {
            get
            {
                if (Time.realtimeSinceStartup - runningAt < 1f) return running;
                runningAt = Time.realtimeSinceStartup;
                try
                {
                    running = process != null && !process.HasExited || Process.GetProcessesByName("RimFpsClient").Length > 0;
                }
                catch (Exception)
                {
                    running = false;
                }
                return running;
            }
        }

        /// <summary>At RimWorld's start.</summary>
        public static void Start(string modRoot)
        {
            exe = Find(modRoot);
            if (exe == null)
            {
                Log.Warning($"[RimWorldFPS] no FPS client for {Application.platform} in {Path.Combine(modRoot, "Client")}; start one by hand");
                return;
            }
            Launch();
        }

        private static void Launch()
        {
            if (!BridgeMod.Settings.LaunchClient || exe == null) return;
            try
            {
                if (Process.GetProcessesByName("RimFpsClient").Length > 0)
                {
                    Log.Message("[RimWorldFPS] the FPS client is already running");
                    return;
                }
                if (Application.platform == RuntimePlatform.LinuxPlayer)
                    using (Process.Start(new ProcessStartInfo("chmod", $"+x \"{exe}\"") { UseShellExecute = false, CreateNoWindow = true })) { }
                int self = Process.GetCurrentProcess().Id;
                var info = new ProcessStartInfo(exe, $"--background --parent {self} --address {BridgeMod.Settings.Host}:{BridgeMod.Settings.Port}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                process = Process.Start(info);
                runningAt = -10f;
                Log.Message($"[RimWorldFPS] started the FPS client in the background ({exe}, process {process?.Id})");
            }
            catch (Exception e)
            {
                Log.Warning($"[RimWorldFPS] could not start the FPS client ({exe}): {e.Message}");
                process = null;
            }
        }

        /// <summary>Every frame: started again if it stopped (a few times at most).</summary>
        public static void Watch()
        {
            if (exe == null || !BridgeMod.Settings.LaunchClient || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 5f;
            if (Running || restarts >= MaxRestarts) return;
            restarts++;
            Log.Message($"[RimWorldFPS] the FPS client stopped; starting it again ({restarts}/{MaxRestarts})");
            Launch();
        }

        /// <summary>The dot was clicked while offline: try again now.</summary>
        public static void Restart()
        {
            restarts = 0;
            nextCheck = 0;
            if (exe == null) Log.Warning("[RimWorldFPS] no FPS client for this computer in the mod's Client folder");
            else Launch();
        }

        /// <summary>RimWorld is quitting: the client goes too.</summary>
        public static void Stop()
        {
            try
            {
                if (process != null && !process.HasExited) process.Kill();
            }
            catch (Exception)
            {
            }
            process = null;
        }
    }
}
