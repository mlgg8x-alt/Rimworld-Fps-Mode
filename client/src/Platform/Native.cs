using System.Runtime.InteropServices;

namespace RimFps.Platform;

/// <summary>
/// The two windows on Windows: ours, which RimWorld's "Play in first person" brings up (we
/// tell it our handle in our <c>hello</c>) and which hides while nobody is played; and
/// RimWorld's, which Tab brings up over ours. Elsewhere there is nothing to switch and
/// these do nothing.
/// </summary>
public static class Native
{
    public static bool Windows => OperatingSystem.IsWindows();

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int SwHide = 0, SwShow = 5, SwRestore = 9;
    private const uint SwpShowWindow = 0x0040;

    /// <summary>Puts RimWorld's window over ours, the same size, and in front. False when
    /// Windows would not.</summary>
    public static bool BringUp(long rimworld, long own)
    {
        if (!Windows || rimworld == 0) return false;
        try
        {
            var target = new IntPtr(rimworld);
            if (IsIconic(target)) ShowWindow(target, SwRestore);
            if (own != 0 && GetWindowRect(new IntPtr(own), out Rect r))
                SetWindowPos(target, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SwpShowWindow);
            return SetForegroundWindow(target);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Hides our window, or shows it in front.</summary>
    public static void Show(long own, bool shown)
    {
        if (!Windows || own == 0) return;
        try
        {
            var window = new IntPtr(own);
            ShowWindow(window, shown ? (IsIconic(window) ? SwRestore : SwShow) : SwHide);
            if (shown) SetForegroundWindow(window);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Whether a window still exists (its program still runs).</summary>
    public static bool Exists(long window)
    {
        if (!Windows || window == 0) return true;
        try
        {
            return IsWindow(new IntPtr(window));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    public static bool Foreground(long window)
    {
        if (!Windows || window == 0) return false;
        try
        {
            return GetForegroundWindow() == new IntPtr(window);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
