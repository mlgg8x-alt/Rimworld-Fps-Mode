using System.Runtime.InteropServices;
using System.Text;

namespace RimFps;

/// <summary>
/// The Android app's way in: Android starts raylib's NativeActivity glue, which calls this
/// <c>main</c> once the window is ready. What the client prints goes to Android's log (tag
/// RimWorldFPS, read with <c>adb logcat -s RimWorldFPS</c>) and to client.log in the app's folder.
/// </summary>
public static class AndroidMain
{
    [UnmanagedCallersOnly(EntryPoint = "main")]
    public static int Main(int argc, nint argv)
    {
        try
        {
            Directory.CreateDirectory(Platform.Native.AndroidDataDirectory);
            var log = new Logcat(Path.Combine(Platform.Native.AndroidDataDirectory, "client.log"));
            Console.SetOut(log);
            Console.SetError(log);
            Console.WriteLine($"[fps] Android app starting at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return Program.Main(Array.Empty<string>());
        }
        catch (Exception e)
        {
            Logcat.Write("crashed: " + e);
            return 1;
        }
    }
}

/// <summary>Lines to Android's log and to a file.</summary>
internal sealed class Logcat : TextWriter
{
    private readonly StreamWriter? file;
    private readonly StringBuilder line = new();

    public Logcat(string path)
    {
        try
        {
            file = new StreamWriter(path, false) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            file = null;
        }
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            Flush();
            return;
        }
        if (value != '\r') line.Append(value);
    }

    public override void Flush()
    {
        if (line.Length == 0) return;
        string text = line.ToString();
        line.Clear();
        Write(text, file);
    }

    public static void Write(string text, StreamWriter? file = null)
    {
        __android_log_write(4, "RimWorldFPS", text);
        try
        {
            file?.WriteLine(text);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    [DllImport("log")]
    private static extern int __android_log_write(int priority, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
}
