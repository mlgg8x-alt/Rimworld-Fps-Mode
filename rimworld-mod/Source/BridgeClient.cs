using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RimWorldFPS
{
    /// <summary>
    /// Newline-delimited JSON over one localhost TCP connection.
    /// All socket work happens on a background thread. RimWorld state is not thread-safe,
    /// so this class never touches it: the main thread drains <see cref="Inbound"/> on tick.
    /// </summary>
    public sealed class BridgeClient : IDisposable
    {
        private const int ReconnectDelayMs = 2000;

        private readonly string host;
        private readonly int port;
        private readonly ConcurrentQueue<string> outbound = new ConcurrentQueue<string>();
        private readonly AutoResetEvent outboundSignal = new AutoResetEvent(false);
        /// <summary>Set when the client is disposed: wakes the reconnect wait at once.</summary>
        private readonly ManualResetEvent stopped = new ManualResetEvent(false);
        /// <summary>The connection in use, for <see cref="Dispose"/> to close.</summary>
        private volatile TcpClient current;
        private readonly Thread thread;
        private volatile bool running = true;
        private volatile bool connected;

        /// <summary>Lines received from the client, oldest first. Drain only on the main thread.</summary>
        public readonly ConcurrentQueue<string> Inbound = new ConcurrentQueue<string>();

        /// <summary>Status lines for the main thread to log (Verse.Log is not thread-safe).</summary>
        public readonly ConcurrentQueue<string> StatusMessages = new ConcurrentQueue<string>();

        public bool Connected => connected;

        public BridgeClient(string host, int port)
        {
            this.host = host;
            this.port = port;
            thread = new Thread(Run) { IsBackground = true, Name = "RimWorldFPS bridge" };
            thread.Start();
        }

        /// <summary>Queue one JSON object (no trailing newline) to send. Dropped while disconnected.</summary>
        public void Send(string json)
        {
            if (!connected) return;
            outbound.Enqueue(json);
            outboundSignal.Set();
        }

        private void Run()
        {
            while (running)
            {
                try
                {
                    using (var tcp = new TcpClient())
                    {
                        current = tcp;
                        tcp.NoDelay = true;
                        tcp.Connect(host, port);
                        using (var stream = tcp.GetStream())
                        {
                            connected = true;
                            StatusMessages.Enqueue($"connected to {host}:{port}");
                            Pump(tcp, stream);
                        }
                    }
                }
                catch (Exception e) when (e is SocketException || e is IOException || e is ObjectDisposedException
                                           || e is AggregateException)
                {
                    // The client not running yet, or it went away. Retry quietly.
                }
                finally
                {
                    current = null;
                    if (connected) StatusMessages.Enqueue("disconnected");
                    connected = false;
                    while (outbound.TryDequeue(out _)) { }
                }
                if (running) stopped.WaitOne(ReconnectDelayMs);
            }
        }

        private void Pump(TcpClient tcp, NetworkStream stream)
        {
            var reader = new StreamReader(stream, new UTF8Encoding(false));
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false };
            var readTask = reader.ReadLineAsync();
            while (running && tcp.Connected)
            {
                bool wrote = false;
                while (outbound.TryDequeue(out var line))
                {
                    writer.WriteLine(line);
                    wrote = true;
                }
                if (wrote) writer.Flush();

                if (readTask.IsCompleted)
                {
                    var line = readTask.Result;
                    if (line == null) return; // remote closed
                    if (line.Length > 0) Inbound.Enqueue(line);
                    readTask = reader.ReadLineAsync();
                    continue;
                }
                outboundSignal.WaitOne(5);
            }
        }

        /// <summary>Stops the connection and waits a moment for its thread to end. M19: RimWorld
        /// crashed on quitting while the thread still waited on a socket.</summary>
        public void Dispose()
        {
            running = false;
            stopped.Set();
            outboundSignal.Set();
            try
            {
                current?.Close();
            }
            catch (Exception)
            {
                // Already closed.
            }
            if (Thread.CurrentThread != thread) thread.Join(1000);
        }
    }
}
