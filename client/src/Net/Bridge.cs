using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RimFps.Net;

/// <summary>
/// The client's end of the bridge: it listens on localhost and RimWorld's mod connects,
/// retrying every two seconds, so either can start first. One JSON object a line each way
/// (protocol/PROTOCOL.md). Socket work stays on background threads; the game loop drains
/// <see cref="Inbound"/> and fills the outbound queue through <see cref="Send"/>.
/// </summary>
public sealed class Bridge : IDisposable
{
    public const string DefaultAddress = "127.0.0.1:47820";

    private readonly TcpListener listener;
    private readonly Thread acceptThread;
    private readonly ConcurrentQueue<string> outbound = new();
    private readonly AutoResetEvent outboundSignal = new(false);
    private volatile bool running = true;
    private volatile TcpClient? current;
    /// <summary>Counts connections, so a reader of an old one stops quietly.</summary>
    private int generation;

    /// <summary>Lines from RimWorld, oldest first. Drain on the game loop only.</summary>
    public readonly ConcurrentQueue<string> Inbound = new();
    /// <summary>Things worth printing, from the socket threads.</summary>
    public readonly ConcurrentQueue<string> Status = new();

    public bool Connected => current?.Connected == true;
    /// <summary>Bumped on every new connection, for the game to start afresh.</summary>
    public int Connections { get; private set; }
    public string Address { get; }

    /// <summary>Our own window, sent in our <c>hello</c>, for RimWorld's "Play in first
    /// person" to bring up; "0" where there is none.</summary>
    public Func<string> OwnWindow = () => "0";

    public Bridge(string address)
    {
        Address = address;
        var (host, port) = Parse(address);
        listener = new TcpListener(host, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        // The mod checks whether the port is taken by binding it for a moment: if that lands
        // just as we start, try again shortly rather than fail.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                listener.Start();
                break;
            }
            catch (SocketException) when (attempt < 20)
            {
                Thread.Sleep(150);
            }
        }
        acceptThread = new Thread(Accept) { IsBackground = true, Name = "bridge accept" };
        acceptThread.Start();
    }

    private static (IPAddress, int) Parse(string address)
    {
        int colon = address.LastIndexOf(':');
        string host = colon > 0 ? address[..colon] : "127.0.0.1";
        int port = colon > 0 && int.TryParse(address[(colon + 1)..], out int p) ? p : 47820;
        if (!IPAddress.TryParse(host, out IPAddress? ip)) ip = IPAddress.Loopback;
        return (ip, port);
    }

    /// <summary>Queues one JSON object (no newline). Dropped while nobody is connected.</summary>
    public void Send(string json)
    {
        if (!Connected) return;
        outbound.Enqueue(json);
        outboundSignal.Set();
    }

    /// <summary>Our <c>hello</c>, again: once we have a window to tell RimWorld about.</summary>
    public void SendHello() => Send(Hello());

    private string Hello() =>
        "{\"t\":\"hello\",\"side\":\"fps\",\"protocol\":0,\"window\":\"" + OwnWindow() + "\"}";

    private void Accept()
    {
        while (running)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception) when (!running)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            client.NoDelay = true;
            // A new RimWorld (a save loaded again, RimWorld restarted) replaces the old one.
            try { current?.Close(); } catch (Exception) { }
            current = client;
            int mine = Interlocked.Increment(ref generation);
            Connections++;
            while (outbound.TryDequeue(out _)) { }
            Status.Enqueue($"RimWorld connected from {client.Client.RemoteEndPoint}");
            outbound.Enqueue(Hello());
            new Thread(() => Read(client, mine)) { IsBackground = true, Name = "bridge read" }.Start();
            new Thread(() => Write(client, mine)) { IsBackground = true, Name = "bridge write" }.Start();
        }
    }

    private void Read(TcpClient client, int mine)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false), false, 1 << 16);
            while (running && mine == generation)
            {
                string? line = reader.ReadLine();
                if (line == null) break;
                if (line.Length > 0) Inbound.Enqueue(line);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
        }
        if (mine == generation)
        {
            Status.Enqueue("RimWorld disconnected");
            try { client.Close(); } catch (Exception) { }
        }
    }

    private void Write(TcpClient client, int mine)
    {
        try
        {
            var stream = client.GetStream();
            var utf8 = new UTF8Encoding(false);
            var buffer = new MemoryStream();
            while (running && mine == generation && client.Connected)
            {
                outboundSignal.WaitOne(50);
                buffer.SetLength(0);
                while (outbound.TryDequeue(out string? line))
                {
                    byte[] bytes = utf8.GetBytes(line);
                    buffer.Write(bytes, 0, bytes.Length);
                    buffer.WriteByte((byte)'\n');
                }
                if (buffer.Length > 0)
                {
                    stream.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
                    stream.Flush();
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        running = false;
        outboundSignal.Set();
        try { current?.Close(); } catch (Exception) { }
        try { listener.Stop(); } catch (Exception) { }
    }
}
