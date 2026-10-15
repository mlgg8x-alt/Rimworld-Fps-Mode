using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// A dot in the top right corner, in the menus and in a game: green while the FPS client is
    /// online (running, and in a game connected), red while it is not. Its tooltip says which;
    /// clicking it while red starts the client again.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class StatusDot
    {
        private static readonly Texture2D Dot = MakeDot(32);
        private static readonly Color Online = new Color(0.30f, 0.85f, 0.35f);
        private static readonly Color Offline = new Color(0.90f, 0.22f, 0.20f);

        private static Texture2D MakeDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
                }
            tex.Apply();
            return tex;
        }

        internal static void Draw(bool inGame)
        {
            ClientLauncher.Watch();
            bool running = ClientLauncher.Running;
            bool online = inGame ? BridgeGameComponent.Linked : running;
            var rect = new Rect(UI.screenWidth - 22f, 6f, 14f, 14f);
            Color was = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(rect.ExpandedBy(2f), Dot);
            GUI.color = online ? Online : Offline;
            GUI.DrawTexture(rect, Dot);
            GUI.color = was;
            string tip = online
                ? (inGame ? "RimWorld FPS: online. Select a colonist and turn on \"FPS mode\"." : "RimWorld FPS: the client is running.")
                : running ? "RimWorld FPS: the client is running but not connected yet (it connects when a game loads)."
                : "RimWorld FPS: offline, the client is not running. Click to start it."
                    + (ClientLauncher.Problem != null ? "\n\nProblem: " + ClientLauncher.Problem : "")
                    + "\nClient log: " + ClientLauncher.LogPath;
            TooltipHandler.TipRegion(rect.ExpandedBy(4f), tip);
            if (!online && !running && Widgets.ButtonInvisible(rect.ExpandedBy(4f))) ClientLauncher.Restart();
        }

        [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
        private static class InGame
        {
            private static void Postfix()
            {
                Draw(true);
                HealthCard.Update();
            }
        }

        [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootOnGUI))]
        private static class InMenus
        {
            private static void Postfix() => Draw(false);
        }
    }
}
