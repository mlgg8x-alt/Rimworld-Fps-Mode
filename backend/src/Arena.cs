using System.IO.MemoryMappedFiles;

namespace RimFps.Backend;

/// <summary>
/// The memory both processes share for big data (cells, meshes): a named memory-mapped file
/// (Windows: <c>Local\RimWorldFPS.Arena</c>), so nothing big is copied through the socket and the
/// mod reads it in place. A simple bump allocator, reset per map; the first 64 bytes are a header
/// (magic, version, used bytes).
/// </summary>
public sealed class Arena : IDisposable
{
    public const string Name = @"Local\RimWorldFPS.Arena";
    public const long DefaultSize = 512L << 20;
    private const int Header = 64;
    private readonly MemoryMappedFile file;
    private readonly MemoryMappedViewAccessor view;
    private long used = Header;
    public long Size { get; }

    public Arena(long size = DefaultSize, string? backingFile = null)
    {
        Size = size;
        // Named shared memory is Windows' own; elsewhere (tests) a plain file stands in.
        file = OperatingSystem.IsWindows() && backingFile == null
            ? MemoryMappedFile.CreateOrOpen(Name, size)
            : MemoryMappedFile.CreateFromFile(backingFile ?? Path.Combine(Path.GetTempPath(), "RimWorldFPS.arena"), FileMode.Create, null, size);
        view = file.CreateViewAccessor();
        view.Write(0, 0x53504652); // "RFPS"
        view.Write(4, 1);
        Reset();
    }

    public void Reset()
    {
        used = Header;
        view.Write(8, used);
    }

    /// <summary>Room for <paramref name="bytes"/> bytes (16-byte aligned); its offset.</summary>
    public long Alloc(long bytes)
    {
        long at = (used + 15) & ~15L;
        if (at + bytes > Size) throw new InvalidOperationException($"arena full ({Size >> 20} MB)");
        used = at + bytes;
        view.Write(8, used);
        return at;
    }

    public void Write<T>(long offset, T[] data) where T : struct => view.WriteArray(offset, data, 0, data.Length);
    public void Read<T>(long offset, T[] into) where T : struct => view.ReadArray(offset, into, 0, into.Length);

    public void Dispose()
    {
        view.Dispose();
        file.Dispose();
    }
}
