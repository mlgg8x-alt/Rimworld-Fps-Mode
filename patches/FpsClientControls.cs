using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Verse;

namespace RimWorldFPS
{
    // Integrate Draw() into the existing Mod.DoSettingsWindowContents.
    // Register the existing auto-launched process with Attach() for accurate status.
    public static class FpsClientControls
    {
        private static Process client;
        private static string error = "";
        private static string state = "Stopped";
        public static void Attach(Process process) { client = process; state = process == null ? "Stopped" : "Running"; }
        public static string Status
        {
            get
            {
                try
                {
                    if (client != null && !client.HasExited) return "Running (PID " + client.Id + ")";
                    if (client != null) { client.Dispose(); client = null; state = "Stopped"; }
                }
                catch (Exception ex) { error = ex.ToString(); state = "Error"; }
                return state;
            }
        }
        private static string Executable(string root)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(root, "Client", "win-x64", "RimFpsClient.exe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !Directory.Exists("/system"))
                return Path.Combine(root, "Client", "linux-x64", "RimFpsClient");
            throw new PlatformNotSupportedException("No packaged native FPS client for this platform. RimDroid requires a separate Android-compatible client.");
        }
        public static void Start(string root)
        {
            if (Status.StartsWith("Running")) return;
            state = "Starting"; error = "";
            try
            {
                var path = Executable(root);
                if (!File.Exists(path)) throw new FileNotFoundException("FPS client not found", path);
                var process = Process.Start(new ProcessStartInfo {
                    FileName = path, WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = false, CreateNoWindow = true
                });
                if (process == null) throw new InvalidOperationException("Process.Start returned null");
                Attach(process);
                if (process.HasExited) { state = "Error"; error = "Client exited immediately: " + process.ExitCode; }
            }
            catch (Exception ex) { state = "Error"; error = ex.ToString(); Log.Error("[RimWorldFPS] Client launch: " + error); }
        }
        public static void Restart(string root)
        {
            try
            {
                if (client != null && !client.HasExited) { client.Kill(); client.WaitForExit(2000); }
                if (client != null) client.Dispose();
                client = null; state = "Stopped";
                Start(root);
            }
            catch (Exception ex) { state = "Error"; error = ex.ToString(); }
        }
        public static void Draw(Listing_Standard listing, string modRoot)
        {
            listing.Label("FPS client: " + Status);
            if (listing.ButtonText("Force Start Client")) Start(modRoot);
            if (listing.ButtonText("Restart Client")) Restart(modRoot);
            if (!string.IsNullOrEmpty(error)) listing.Label("Last launch error: " + error);
        }
    }
}
