using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// The client's status in the top right corner, in the menus and in a game: a dot (green
    /// online or ready, yellow starting, red offline), a word for it, details and the client's
    /// log path on hover, and a button that force starts the client.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class StatusDot
    {
        private static readonly Texture2D Dot = MakeDot(32);
        private static readonly Color Online = new Color(0.30f, 0.85f, 0.35f);
        private static readonly Color Offline = new Color(0.90f, 0.22f, 0.20f);
        private static readonly Color Starting = new Color(0.95f, 0.75f, 0.2f);

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

        /// <summary>The indicator in the top right corner: a dot and a word for the client's
        /// state, and a button that force starts it.</summary>
        internal static void Draw(bool inGame)
        {
            ClientLauncher.Watch();
            ClientLauncher.State state = ClientLauncher.Now(inGame && BridgeGameComponent.Linked);
            string label = ClientLauncher.Label(state);
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color was = GUI.color;
            Text.Font = GameFont.Tiny;
            float labelWidth = Text.CalcSize(label).x, buttonWidth = 74f;
            var box = new Rect(UI.screenWidth - (labelWidth + buttonWidth + 40f), 4f, labelWidth + buttonWidth + 36f, 22f);
            Widgets.DrawBoxSolid(box, new Color(0.08f, 0.09f, 0.1f, 0.78f));
            var dot = new Rect(box.x + 6f, box.y + 5f, 12f, 12f);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(dot.ExpandedBy(1.5f), Dot);
            GUI.color = state == ClientLauncher.State.Online || state == ClientLauncher.State.Waiting ? Online
                : state == ClientLauncher.State.Starting ? Starting : Offline;
            GUI.DrawTexture(dot, Dot);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            var text = new Rect(dot.xMax + 6f, box.y, labelWidth + 4f, box.height);
            Widgets.Label(text, label);
            TooltipHandler.TipRegion(new Rect(box.x, box.y, text.xMax - box.x, box.height), ClientLauncher.Describe(state, inGame));
            var button = new Rect(box.xMax - buttonWidth - 3f, box.y + 2f, buttonWidth, box.height - 4f);
            Text.Anchor = TextAnchor.MiddleCenter;
            if (Widgets.ButtonText(button, state == ClientLauncher.State.Offline ? "Start" : "Restart"))
                ClientLauncher.ForceStart();
            TooltipHandler.TipRegion(button, "Force start the FPS client: stops any client that is running and starts a fresh one.");
            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = was;
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
