using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>Mod entry point: applies Harmony patches and owns the bridge settings.</summary>
    public class BridgeMod : Mod
    {
        public const string HarmonyId = "rimworldfps.mode";

        public static BridgeSettings Settings;

        public BridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BridgeSettings>();
            new Harmony(HarmonyId).PatchAll();
            Application.quitting += BridgeGameComponent.Shutdown;
            Application.quitting += ClientLauncher.Stop;
            ClientLauncher.Start(content.RootDir);
            RimView.RepairPrefs();
            Log.Message($"[RimWorldFPS] loaded, bridge target {Settings.Host}:{Settings.Port}");
        }

        public override string SettingsCategory() => "RimWorld FPS Mode";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("FPS client address (RimWorld connects, the client listens)");
            Settings.Host = list.TextEntryLabeled("Host", Settings.Host);
            string port = Settings.Port.ToString();
            list.TextFieldNumericLabeled("Port", ref Settings.Port, ref port, 1, 65535);
            list.CheckboxLabeled("Start the FPS client in the background with RimWorld", ref Settings.LaunchClient);
            list.CheckboxLabeled("Connect automatically when a game loads", ref Settings.AutoConnect);
            list.CheckboxLabeled("Hide RimWorld's UI in FPS mode (off keeps it up for debugging)", ref Settings.HideUiInMinimap);
            list.End();
        }
    }

    public class BridgeSettings : ModSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 47820;
        public bool AutoConnect = true;
        /// <summary>The client in the mod's Client folder starts with RimWorld.</summary>
        public bool LaunchClient = true;
        /// <summary>RimWorld's UI hidden while its window is the client's minimap. Off for now, so
        /// the UI stays up for debugging.</summary>
        public bool HideUiInMinimap;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Host, "host", "127.0.0.1");
            Scribe_Values.Look(ref Port, "port", 47820);
            Scribe_Values.Look(ref AutoConnect, "autoConnect", true);
            Scribe_Values.Look(ref LaunchClient, "launchClient", true);
            Scribe_Values.Look(ref HideUiInMinimap, "hideUiInMinimap", false);
        }
    }
}
