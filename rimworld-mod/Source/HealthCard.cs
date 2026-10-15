using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldFPS
{
    /// <summary>
    /// The played colonist's Health tab, as RimWorld draws it, for the client's HUD: drawn about
    /// once a second into a picture off the screen and sent when it changes. It is the colonist's
    /// own health tab (the one in its inspector, so whatever a mod such as Nice Health Tab does to
    /// it shows too) while the colonist is selected, else <see cref="HealthCardUtility"/>'s card,
    /// which the tab draws. Drawn in pieces no bigger than RimWorld's window, which is small while
    /// it is the client's minimap and would cut the card off. Main thread, from the UI.
    /// </summary>
    internal static class HealthCard
    {
        private const float Seconds = 1f;
        /// <summary>Drawn again at least this often, for what the tab shows beyond the health
        /// report (a mod's extras), else only when the health report changed.</summary>
        private const float Anyway = 8f;
        private static float due, drawnAt = -100f;
        private static int drawnRevision = -1;
        private static Pawn drawnPawn;
        private static int sentHash;
        private static string pending;
        private static Texture2D image;
        private static bool failed;
        private static readonly System.Reflection.MethodInfo FillTab = AccessTools.Method(typeof(InspectTabBase), "FillTab");

        public static void Reset()
        {
            drawnPawn = null;
            drawnRevision = -1;
            sentHash = 0;
            pending = null;
            due = 0;
        }

        /// <summary>The <c>health_card</c> line waiting to go, once.</summary>
        public static string Take()
        {
            string line = pending;
            pending = null;
            return line;
        }

        /// <summary>From the UI, every frame: draws the card when it is due.</summary>
        public static void Update()
        {
            if (failed || Event.current == null || Event.current.type != EventType.Repaint || !BridgeGameComponent.Linked) return;
            if (Time.realtimeSinceStartup < due) return;
            due = Time.realtimeSinceStartup + Seconds;
            Map map = Find.CurrentMap;
            Pawn pawn = map != null ? PlayerPawn.Current(map) : null;
            if (pawn == null) return;
            // Reading the picture back stalls the GPU: only when something changed.
            if (pawn == drawnPawn && StatusReport.Revision == drawnRevision && Time.realtimeSinceStartup - drawnAt < Anyway) return;
            drawnPawn = pawn;
            drawnRevision = StatusReport.Revision;
            drawnAt = Time.realtimeSinceStartup;
            try
            {
                Draw(pawn);
            }
            catch (Exception e)
            {
                failed = true;
                Log.Warning("[RimWorldFPS] could not draw the health tab for the client; it shows its own instead: " + e);
            }
        }

        private static void Draw(Pawn pawn)
        {
            // The colonist's own health tab while they are selected (it reads the selection).
            InspectTabBase tab = null;
            if (Find.Selector.SingleSelectedThing == pawn && FillTab != null)
                tab = pawn.def.inspectorTabsResolved?.FirstOrDefault(t => t is ITab_Pawn_Health)
                    ?? pawn.def.inspectorTabsResolved?.FirstOrDefault(t => t.GetType().Name.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0);
            Vector2 size = tab != null ? Traverse.Create(tab).Field("size").GetValue<Vector2>() : new Vector2(ITab_Pawn_Health.Width, 430f);
            int width = Mathf.Clamp(Mathf.RoundToInt(size.x), 200, 1400), height = Mathf.Clamp(Mathf.RoundToInt(size.y), 150, 1100);
            int tileW = Mathf.Max(64, Mathf.Min(width, Screen.width)), tileH = Mathf.Max(64, Mathf.Min(height, Screen.height));
            if (image == null || image.width != width || image.height != height)
            {
                if (image != null) UnityEngine.Object.Destroy(image);
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
            RenderTexture rt = RenderTexture.GetTemporary(tileW, tileH, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Matrix4x4 matrix = GUI.matrix;
            Color color = GUI.color;
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            bool upright = true;
            try
            {
                RenderTexture.active = rt;
                GUI.matrix = Matrix4x4.identity;
                for (int ty = 0; ty < height; ty += tileH)
                    for (int tx = 0; tx < width; tx += tileW)
                    {
                        GL.Clear(true, true, Color.clear);
                        GL.PushMatrix();
                        GL.LoadPixelMatrix(0, tileW, tileH, 0);
                        GUI.BeginGroup(new Rect(-tx, -ty, width, height));
                        Widgets.DrawWindowBackground(new Rect(0, 0, width, height));
                        if (tab != null) FillTab.Invoke(tab, null);
                        else HealthCardUtility.DrawPawnHealthCard(new Rect(0, 20f, width, height - 20f), pawn, false, true, pawn);
                        GUI.color = color;
                        // A mark in the top left corner, to tell which way up the picture reads back.
                        if (tx == 0 && ty == 0) Widgets.DrawBoxSolid(new Rect(0, 0, 2, 2), Color.magenta);
                        GUI.EndGroup();
                        GL.PopMatrix();
                        int w = Mathf.Min(tileW, width - tx), h = Mathf.Min(tileH, height - ty);
                        image.ReadPixels(new Rect(0, tileH - h, w, h), tx, height - ty - h);
                        if (tx == 0 && ty == 0)
                        {
                            Color top = image.GetPixel(0, height - 1), bottom = image.GetPixel(0, height - h);
                            upright = !(IsMark(bottom) && !IsMark(top));
                        }
                    }
                image.Apply();
            }
            finally
            {
                GUI.matrix = matrix;
                GUI.color = color;
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
            Color32[] pixels = image.GetPixels32();
            if (!upright)
            {
                // Read back upside down on this graphics card: turned over row by row (per tile
                // it would be wrong only when the card is taller than the window; rare).
                var turned = new Color32[pixels.Length];
                for (int y = 0; y < height; y++) Array.Copy(pixels, y * width, turned, (height - 1 - y) * width, width);
                pixels = turned;
            }
            // The mark goes again.
            for (int y = height - 2; y < height; y++)
                for (int x = 0; x < 2; x++)
                    pixels[y * width + x] = pixels[(height - 3) * width + 2];
            int hash = 17;
            for (int i = 0; i < pixels.Length; i += 7) hash = hash * 31 + pixels[i].r + (pixels[i].g << 8) + (pixels[i].b << 16);
            if (hash == sentHash) return;
            sentHash = hash;
            image.SetPixels32(pixels);
            image.Apply();
            string png = Convert.ToBase64String(image.EncodeToPNG());
            pending = Json.Msg("health_card").Str("pawn", pawn.ThingID).Num("w", width).Num("h", height).Str("png", png).ToString();
        }

        private static bool IsMark(Color c) => c.r > 0.9f && c.b > 0.9f && c.g < 0.15f;
    }
}
