using System;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M9: RimWorld inside MW2. While MW2 has the focus, RimWorld's camera follows the MW2
    /// player (the drafted colonist they play, or where they fly while spectating). M16: and
    /// RimWorld's own window is MW2's minimap: shrunk to a square in the top left corner of
    /// MW2's screen, kept on top, without its interface, so it is RimWorld itself at its own
    /// quality and speed rather than a copy. Tab in MW2 brings RimWorld's window up whole over
    /// MW2's, free to look anywhere; Tab in RimWorld goes back (<see cref="BackToMw2"/>).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimView
    {
        /// <summary>How many times further out RimWorld's camera looks while it is the minimap,
        /// than RimWorld's own zoom (M18: 2 was too far out).</summary>
        private const float Zoom = 1f;
        /// <summary>The minimap's side, of MW2's height (M17: 0.3 was too small).</summary>
        private const float Side = 0.5f;
        /// <summary>Seconds RimWorld may hold the focus while overlaid before MW2 gets it back,
        /// so a Tab to RimWorld (<c>rimworld_shown</c>) is heard first.</summary>
        private const float GiveBackAfter = 0.15f;
        /// <summary>Seconds between checks that the minimap window is still where it should be.</summary>
        private const float PlaceSeconds = 0.5f;
        /// <summary>Seconds to wait for Unity to take the new size before placing it anyway.</summary>
        private const float ResizeSeconds = 1f;
        /// <summary>Where MW2 says it flies is forgotten after this many seconds of silence.</summary>
        private const float SpectateSeconds = 1f;

        /// <summary>RimWorld's own zoom before the minimap zoomed out, to put back.</summary>
        private static float? ownSize;

        private static readonly System.Reflection.MethodInfo ApplyCamera =
            AccessTools.Method(typeof(CameraDriver), "ApplyPositionToGameObject");

        private static Vector3? spectating;
        private static float spectatedAt;
        /// <summary>MW2's window, to go back to on Tab.</summary>
        private static IntPtr mw2Window;

        /// <summary>RimWorld's window as it was before it became the minimap, to put back.</summary>
        private static Saved saved;
        private static bool overlaid;
        private static Rect target;
        private static float overlaidAt, lastPlace, focusedAt = -1f;
        private static bool placed;
        private static Action<string> sendLast;

        private sealed class Saved
        {
            public FullScreenMode mode;
            public int width, height;
            public Rectangle rect;
            public long style, exStyle;
            public bool screenshot;
        }

        /// <summary>A <c>spectate</c>: where the MW2 player flies, x and z in cells.</summary>
        public static void Spectate(float x, float z)
        {
            spectating = new Vector3(x, 0f, z);
            spectatedAt = Time.realtimeSinceStartup;
        }

        /// <summary>A <c>rimworld_shown</c>: MW2 brought RimWorld's window up; <paramref name="back"/>
        /// is MW2's window, for Tab to go back to. The colonist MW2 plays is selected, so its
        /// "Stop controlling" button is right there (M12).</summary>
        public static void Shown(string back)
        {
            Window(back);
            // The player asked for RimWorld: it is no longer the minimap, nor becomes it again
            // before its window has come up.
            Drop();
            shownAt = Time.realtimeSinceStartup;
            Pawn pawn = PlayerPawn.Current(PawnSnapshot.MapInView());
            if (pawn == null || Find.Selector == null) return;
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
        }

        /// <summary>MW2's own window, from its <c>hello</c> (M12) or a <c>rimworld_shown</c>.</summary>
        public static void Window(string handle)
        {
            if (long.TryParse(handle, out long value) && value != 0) mw2Window = new IntPtr(value);
        }

        /// <summary>"Control in MW2" (M12): MW2's window comes to the front. RimWorld has the
        /// focus, so Windows lets it hand the focus on.</summary>
        public static void ToMw2()
        {
            if (!Windows || mw2Window == IntPtr.Zero) return;
            ShowWindow(mw2Window, IsIconic(mw2Window) ? SwRestore : SwShow);
            SetForegroundWindow(mw2Window);
        }

        /// <summary>Whether RimWorld's camera follows the MW2 player now: while MW2 has the focus,
        /// and always while RimWorld is its minimap (M18: the minimap stood still, whatever
        /// Unity thought of the focus).</summary>
        private static bool Following => BridgeGameComponent.Linked && (overlaid || !Focused);

        /// <summary>Whether RimWorld's window is the one in front. M19: asked of Windows, not
        /// Unity, whose <c>Application.isFocused</c> stayed true while MW2 was in front: after
        /// Tab back to MW2 the minimap never came back, and RimWorld kept handing MW2 the focus
        /// it already had, which let go of MW2's mouse.</summary>
        private static bool Focused
        {
            get
            {
                if (!Windows) return Application.isFocused;
                IntPtr own = new IntPtr(OwnWindow());
                return own != IntPtr.Zero && GetForegroundWindow() == own;
            }
        }

        /// <summary>MW2's Escape menu is open (M18): the minimap hides so it covers nothing.</summary>
        private static bool menuOpen, hidden;

        /// <summary>A <c>menu</c>: MW2's Escape menu opened or closed.</summary>
        public static void Menu(bool open) => menuOpen = open;

        /// <summary>Times the camera was put on the MW2 player, and when that was last logged.</summary>
        private static int follows, followLogs;
        private static float followLoggedAt;

        /// <summary>Where the camera should look: the played colonist, or where MW2 flies.</summary>
        internal static Vector3? Target(Map map)
        {
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn != null) return pawn.DrawPos;
            if (spectating.HasValue && Time.realtimeSinceStartup - spectatedAt < SpectateSeconds) return spectating;
            return null;
        }

        /// <summary>After RimWorld moves its camera: back onto the MW2 player while MW2 has the focus.</summary>
        internal static void Follow()
        {
            if (!Following || Current.ProgramState != ProgramState.Playing) return;
            Map map = PawnSnapshot.MapInView();
            if (map == null || Find.CameraDriver == null) return;
            Vector3? target = Target(map);
            if (!target.HasValue) return;
            CameraDriver driver = Find.CameraDriver;
            driver.JumpToCurrentMapLoc(target.Value);
            follows++;
            // At once, not next frame (M17: the minimap lagged behind the colonist).
            ApplyCamera?.Invoke(driver, null);
            if (!ownSize.HasValue) ownSize = driver.RootSize;
            float wide = Mathf.Min(ownSize.Value * Zoom, driver.config.sizeRange.max);
            if (Mathf.Abs(driver.RootSize - wide) > 0.01f)
            {
                driver.SetRootSize(wide);
                ApplyCamera?.Invoke(driver, null);
            }
        }

        /// <summary>While RimWorld is the minimap, now and then into the log: where its camera
        /// looks and where the played colonist is, to tell a minimap that stands still.</summary>
        private static void LogFollowing(float now)
        {
            if (followLogs >= 6 || now - followLoggedAt < 5f) return;
            followLoggedAt = now;
            followLogs++;
            Map map = PawnSnapshot.MapInView();
            Vector3? target = map == null ? null : Target(map);
            Vector3 camera = Find.Camera != null ? Find.Camera.transform.position : Vector3.zero;
            Log.Message($"[RimWorldMW2] minimap: camera at ({camera.x:F1}, {camera.z:F1}) size {Find.CameraDriver?.RootSize:F1}, "
                + $"colonist at ({target?.x:F1}, {target?.z:F1}), followed {follows} times, in front {Focused}"
                + (ApplyCamera == null ? ", camera not applied at once" : ""));
        }

        /// <summary>RimWorld in front again: its own zoom back.</summary>
        private static void Unzoom()
        {
            if (!ownSize.HasValue || Find.CameraDriver == null) return;
            Find.CameraDriver.SetRootSize(ownSize.Value);
            ownSize = null;
        }

        /// <summary>Once a frame from <see cref="BridgeGameComponent"/>: Tab back to MW2, and
        /// RimWorld's window in MW2's corner while a colonist is played there.</summary>
        public static void Update(Action<string> send)
        {
            sendLast = send;
            if (Focused && mw2Window != IntPtr.Zero && Input.GetKeyDown(KeyCode.Tab))
                BackToMw2();
            if (!overlaid && !Following && Current.ProgramState == ProgramState.Playing) Unzoom();
            float now = Time.realtimeSinceStartup;
            if (overlaid)
            {
                // M17: the minimap goes only when the player leaves it: Tab (Shown), letting
                // the colonist go, MW2 hidden, or another program in front for a while. Not
                // when Windows shuffles the focus for a moment: putting RimWorld back to full
                // screen then took the focus from MW2 every few seconds.
                if (!StillWanted(now))
                {
                    Drop();
                    return;
                }
                if (Focused)
                {
                    // RimWorld took the focus without the player asking (Tab drops the minimap
                    // first): MW2 gets it back.
                    if (focusedAt < 0f) focusedAt = now;
                    if (now - focusedAt >= GiveBackAfter)
                    {
                        focusedAt = -1f;
                        if (IsIconic(mw2Window)) ShowWindow(mw2Window, SwRestore);
                        SetForegroundWindow(mw2Window);
                    }
                }
                else focusedAt = -1f;
                CloseInterface();
                if (Hide(menuOpen)) return;
                KeepPlaced();
                // Here too, not only after RimWorld moves its camera (M18).
                Follow();
                LogFollowing(now);
                return;
            }
            focusedAt = -1f;
            elsewhereSince = -1f;
            if (now - shownAt >= ShownSeconds && WantOverlay()) Overlay(send);
        }

        /// <summary>Seconds after a Tab to RimWorld before it can be the minimap again.</summary>
        private const float ShownSeconds = 1f;
        private static float shownAt = -10f;

        /// <summary>Seconds another program may be in front before the minimap goes.</summary>
        private const float ElsewhereSeconds = 1f;
        private static float elsewhereSince = -1f;

        /// <summary>Whether the minimap should stay: a colonist is still played in MW2, whose
        /// window is up, and no other program has been in front for <see cref="ElsewhereSeconds"/>.</summary>
        private static bool StillWanted(float now)
        {
            if (!Windows || mw2Window == IntPtr.Zero || !IsWindow(mw2Window)) return false;
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (PlayerPawn.Current(PawnSnapshot.MapInView()) == null) return false;
            if (!IsWindowVisible(mw2Window) || IsIconic(mw2Window)) return false;
            IntPtr front = GetForegroundWindow();
            IntPtr own = new IntPtr(OwnWindow());
            if (front == IntPtr.Zero || front == mw2Window || front == own)
            {
                elsewhereSince = -1f;
                return true;
            }
            if (elsewhereSince < 0f) elsewhereSince = now;
            return now - elsewhereSince < ElsewhereSeconds;
        }

        /// <summary>RimWorld's menus closed while it is the minimap (M17: keys pressed in the
        /// moment it had the focus opened the architect menu over it).</summary>
        private static void CloseInterface()
        {
            try
            {
                if (Find.MainTabsRoot?.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab(false);
                if (Find.DesignatorManager?.SelectedDesignator != null) Find.DesignatorManager.Deselect();
            }
            catch (Exception)
            {
                // Not while a game is loading.
            }
        }

        /// <summary>Whether RimWorld should be MW2's minimap now: a colonist is played, and MW2's
        /// window is in front (not RimWorld's, nor anything else's).</summary>
        private static bool WantOverlay()
        {
            if (!Windows || !Following || mw2Window == IntPtr.Zero || menuOpen) return false;
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (PlayerPawn.Current(PawnSnapshot.MapInView()) == null) return false;
            if (!IsWindowVisible(mw2Window) || IsIconic(mw2Window)) return false;
            IntPtr front = GetForegroundWindow();
            IntPtr own = new IntPtr(OwnWindow());
            // Ours on top of MW2 counts as MW2's, as a click on it would make it.
            return front == mw2Window || (own != IntPtr.Zero && front == own && overlaid);
        }

        /// <summary>Shrinks RimWorld's window into MW2's top left corner, on top, its interface
        /// hidden.</summary>
        private static void Overlay(Action<string> send)
        {
            IntPtr own = new IntPtr(OwnWindow());
            if (own == IntPtr.Zero || !Corner(out Rect corner)) return;
            // M20: a fullscreen RimWorld is minimised by Windows once MW2 is in front, and a
            // minimised window stays out of sight whatever its size: it comes back first,
            // without taking the focus.
            bool iconic = IsIconic(own);
            if (iconic) ShowWindow(own, SwShowNoActivate);
            GetWindowRect(own, out Rectangle rect);
            FullScreenMode mode = Screen.fullScreenMode;
            bool full = mode != FullScreenMode.Windowed;
            saved = new Saved
            {
                mode = mode,
                // Full screen is the whole display, whatever Unity reports while minimised.
                width = full ? Display.main.systemWidth : Screen.width,
                height = full ? Display.main.systemHeight : Screen.height,
                rect = rect,
                style = GetWindowLongPtr(own, GwlStyle).ToInt64(),
                exStyle = GetWindowLongPtr(own, GwlExStyle).ToInt64(),
                screenshot = Find.UIRoot?.screenshotMode?.Active ?? false,
            };
            target = corner;
            int side = (int)target.width;
            if (Find.UIRoot?.screenshotMode != null) Find.UIRoot.screenshotMode.Active = true;
            Screen.SetResolution(side, side, FullScreenMode.Windowed);
            overlaid = true;
            placed = false;
            follows = 0;
            followLogs = 0;
            followLoggedAt = Time.realtimeSinceStartup;
            overlaidAt = Time.realtimeSinceStartup;
            send(Json.Msg("overlay").Bool("shown", true).ToString());
            Log.Message($"[RimWorldMW2] minimap: RimWorld's window as MW2's minimap, {side}px square in its top left corner"
                + $" (was {mode} {saved.width}x{saved.height}{(iconic ? ", minimised" : "")})");
        }

        /// <summary>The minimap out of sight while MW2's Escape menu is open, and back after
        /// (M18); hidden rather than put back to full size, which took MW2's focus. True while
        /// hidden.</summary>
        private static bool Hide(bool hide)
        {
            if (hide == hidden) return hidden;
            IntPtr own = new IntPtr(OwnWindow());
            if (own == IntPtr.Zero) return false;
            hidden = hide;
            if (hide) ShowWindow(own, SwHide);
            else
            {
                ShowWindow(own, SwShowNoActivate);
                placed = false;
            }
            return hidden;
        }

        /// <summary>Puts the minimap window in its corner, borderless and on top, once Unity has
        /// taken its size, and again whenever it has moved or dropped behind.</summary>
        private static void KeepPlaced()
        {
            float now = Time.realtimeSinceStartup;
            int side = (int)target.width;
            if (!placed && (Screen.width != side || Screen.height != side) && now - overlaidAt < ResizeSeconds) return;
            if (placed && now - lastPlace < PlaceSeconds) return;
            lastPlace = now;
            IntPtr own = new IntPtr(OwnWindow());
            if (own == IntPtr.Zero) return;
            if (IsIconic(own))
            {
                ShowWindow(own, SwShowNoActivate);
                placed = false;
            }
            // Unity can put a window that was full screen back to full screen as it comes
            // back (M20): windowed again.
            if (Screen.fullScreenMode != FullScreenMode.Windowed && now - overlaidAt >= ResizeSeconds)
            {
                Screen.SetResolution(side, side, FullScreenMode.Windowed);
                overlaidAt = now;
                placed = false;
                return;
            }
            // MW2's window moved or changed size (M17: it goes fullscreen just after the
            // minimap is made, which left it small in the middle of the screen).
            if (Corner(out Rect corner) && corner != target)
            {
                target = corner;
                side = (int)target.width;
                if (Screen.width != side || Screen.height != side) Screen.SetResolution(side, side, FullScreenMode.Windowed);
                overlaidAt = now;
                placed = false;
            }
            long style = GetWindowLongPtr(own, GwlStyle).ToInt64();
            long popup = (style & ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox)) | WsPopup;
            long ex = GetWindowLongPtr(own, GwlExStyle).ToInt64();
            long tool = ex | WsExToolWindow | WsExNoActivate | WsExTopmost;
            GetWindowRect(own, out Rectangle at);
            bool right = style == popup && ex == tool && at.Left == (int)target.x && at.Top == (int)target.y
                && at.Right - at.Left == side && at.Bottom - at.Top == side;
            if (right && placed) return;
            if (style != popup) SetWindowLongPtr(own, GwlStyle, new IntPtr(popup));
            if (ex != tool) SetWindowLongPtr(own, GwlExStyle, new IntPtr(tool));
            SetWindowPos(own, HwndTopmost, (int)target.x, (int)target.y, side, side,
                SwpNoActivate | SwpFrameChanged | SwpShowWindow);
            placed = true;
        }

        /// <summary>Where the minimap goes: the top left corner of what MW2 draws (its client
        /// area, on the screen), a square <see cref="Side"/> of its height.</summary>
        private static bool Corner(out Rect corner)
        {
            corner = default;
            if (!GetClientRect(mw2Window, out Rectangle client)) return false;
            var origin = new Point();
            if (!ClientToScreen(mw2Window, ref origin)) return false;
            int height = client.Bottom - client.Top;
            if (height <= 0) return false;
            int side = Mathf.Max(200, Mathf.RoundToInt(height * Side));
            corner = new Rect(origin.X, origin.Y, side, side);
            return true;
        }

        /// <summary>RimWorld's window back as it was: its size, place, frame and interface.</summary>
        public static void Drop()
        {
            if (!overlaid) return;
            Hide(false);
            overlaid = false;
            droppedAt = Time.realtimeSinceStartup;
            focusedAt = -1f;
            sendLast?.Invoke(Json.Msg("overlay").Bool("shown", false).ToString());
            Saved was = saved;
            saved = null;
            if (was == null) return;
            IntPtr own = new IntPtr(OwnWindow());
            if (own != IntPtr.Zero)
            {
                SetWindowLongPtr(own, GwlStyle, new IntPtr(was.style));
                SetWindowLongPtr(own, GwlExStyle, new IntPtr(was.exStyle));
                SetWindowPos(own, HwndNoTopmost, was.rect.Left, was.rect.Top, was.rect.Right - was.rect.Left,
                    was.rect.Bottom - was.rect.Top, SwpNoActivate | SwpFrameChanged);
            }
            Screen.SetResolution(was.width, was.height, was.mode);
            if (Find.UIRoot?.screenshotMode != null) Find.UIRoot.screenshotMode.Active = was.screenshot;
        }

        /// <summary>Seconds after the minimap goes during which RimWorld's window is still
        /// settling back to its own size.</summary>
        private const float SettleSeconds = 3f;
        private static float droppedAt = -10f;

        /// <summary>Whether RimWorld may save its window size as the player's: not while it is
        /// the minimap, nor just after (M20: RimWorld saved the minimap's 605x605 as the
        /// player's resolution, and started at that size, full screen, from then on).</summary>
        internal static bool SizeIsOwn => !overlaid && Time.realtimeSinceStartup - droppedAt >= SettleSeconds;

        /// <summary>At start: a resolution the minimap left in the player's settings (a small
        /// square) back to the display's own.</summary>
        internal static void RepairPrefs()
        {
            try
            {
                int w = Prefs.ScreenWidth, h = Prefs.ScreenHeight;
                if (w != h || w >= 1024 || w <= 0) return;
                int width = Display.main.systemWidth, height = Display.main.systemHeight;
                if (width < 1024 || height < 768) return;
                Prefs.ScreenWidth = width;
                Prefs.ScreenHeight = height;
                if (Prefs.UIScale < 1f) Prefs.UIScale = 1f;
                Prefs.Save();
                ResolutionUtility.SetResolutionRaw(width, height, Prefs.FullScreen && !ResolutionUtility.BorderlessFullscreen);
                Log.Message($"[RimWorldMW2] screen size {w}x{h} was the minimap's, not yours: back to {width}x{height}");
            }
            catch (Exception e)
            {
                Log.Warning("[RimWorldMW2] could not repair the screen size: " + e.Message);
            }
        }

        /// <summary>Tab in RimWorld: back to MW2's window, while MW2 plays a colonist.</summary>
        private static void BackToMw2()
        {
            if (PlayerPawn.Current(PawnSnapshot.MapInView()) == null) return;
            if (!Windows) return;
            if (IsIconic(mw2Window)) ShowWindow(mw2Window, SwRestore);
            SetForegroundWindow(mw2Window);
        }

        private static IntPtr ownWindow;

        /// <summary>RimWorld's own window, for MW2 to bring up on Tab; 0 where there is none.</summary>
        public static long OwnWindow()
        {
            if (!Windows) return 0;
            if (ownWindow != IntPtr.Zero && IsWindow(ownWindow)) return ownWindow.ToInt64();
            uint us = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var name = new StringBuilder(64);
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint process);
                if (process != us || !IsWindowVisible(window)) return true;
                name.Length = 0;
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                found = window;
                return false;
            }, IntPtr.Zero);
            ownWindow = found;
            return found.ToInt64();
        }

        private static bool Windows => Application.platform == RuntimePlatform.WindowsPlayer;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle
        {
            public int Left, Top, Right, Bottom;
        }

        private const int SwRestore = 9;
        private const int SwShow = 5;
        private const int SwHide = 0;
        private const int SwShowNoActivate = 4;
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const long WsPopup = 0x80000000L;
        private const long WsCaption = 0x00C00000L;
        private const long WsThickFrame = 0x00040000L;
        private const long WsSysMenu = 0x00080000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsExTopmost = 0x00000008L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExNoActivate = 0x08000000L;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rectangle rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rectangle rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }

    /// <summary>RimWorld keeps the player's window size in their settings; the minimap's is not
    /// theirs (M20).</summary>
    [HarmonyPatch(typeof(ResolutionUtility), nameof(ResolutionUtility.Update))]
    internal static class KeepOwnScreenSize
    {
        private static bool Prefix() => RimView.SizeIsOwn;
    }

    /// <summary>RimWorld moves its camera (keys, edge scrolling, dragging); the MW2 player's
    /// position wins while MW2 has the focus.</summary>
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.Update))]
    internal static class FollowMw2Player
    {
        private static void Postfix() => RimView.Follow();
    }
}
