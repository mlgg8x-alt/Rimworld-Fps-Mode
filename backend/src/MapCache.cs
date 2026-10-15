using System.Runtime.InteropServices;

namespace RimFps.Backend;

/// <summary>
/// Maps kept on disk between sessions (<c>%LocalAppData%\RimWorldFPS\cache</c>), keyed by save,
/// map and mod list, so the home map comes back instantly: the home map is always kept; other
/// maps (caravan sites, quests) only the most recent few, and they take a short build when new.
/// </summary>
public sealed class MapCache
{
    private const int KeepOthers = 3;
    private readonly string dir;

    public MapCache(string? dir = null)
    {
        this.dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RimWorldFPS", "cache");
        Directory.CreateDirectory(this.dir);
    }

    public static string Key(string saveId, int mapId, uint modsHash) => $"{Safe(saveId)}_{mapId}_{modsHash:x8}";

    private string PathOf(string key, bool home) => Path.Combine(dir, (home ? "home_" : "map_") + key + ".cells");

    /// <summary>The cached cells of a map, if the cache has them and they match its size.</summary>
    public Cell[]? Load(string key, bool home, int width, int height)
    {
        string path = PathOf(key, home);
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != width * height * Marshal.SizeOf<Cell>()) return null;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return MemoryMarshal.Cast<byte, Cell>(bytes).ToArray();
    }

    public void Save(string key, bool home, Cell[] cells)
    {
        File.WriteAllBytes(PathOf(key, home), MemoryMarshal.AsBytes(cells.AsSpan()).ToArray());
        if (home) return;
        // Other maps: only the most recent few.
        foreach (var old in new DirectoryInfo(dir).GetFiles("map_*.cells").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepOthers))
            old.Delete();
    }

    private static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
