using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TGLL {
    public sealed class SocketRequest {
        public string Json;
        public string Response;
        public volatile bool Cancelled;
        public DateTime Deadline;
        public readonly ManualResetEventSlim Completed = new ManualResetEventSlim(false);
        public void Reply(string json) { Response = json; Completed.Set(); }
    }
    // Transport only. All account operations run on the UI thread through a queue.
    public sealed class SocketServer : IDisposable {
        private readonly TcpListener listener;
        private readonly SemaphoreSlim slots = new SemaphoreSlim(8, 8);
        private readonly ConcurrentQueue<SocketRequest> requests = new ConcurrentQueue<SocketRequest>();
        private readonly ConcurrentDictionary<TcpClient, byte> clients = new ConcurrentDictionary<TcpClient, byte>();
        private volatile bool running;
        private Thread thread;
        public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
        public SocketServer(int port) { listener = new TcpListener(IPAddress.Loopback, port); }
        public void Start() {
            listener.Start(8); running = true;
            thread = new Thread(Accept); thread.IsBackground = true; thread.Start();
        }
        public bool TryTake(out SocketRequest request) { return requests.TryDequeue(out request); }
        private void Accept() {
            while (running) {
                TcpClient client = null;
                try {
                    client = listener.AcceptTcpClient();
                    if (!slots.Wait(0)) { client.Close(); continue; }
                    clients.TryAdd(client, 0);
                    ThreadPool.QueueUserWorkItem(Handle, client);
                } catch { if (client != null) client.Close(); if (!running) return; }
            }
        }
        private void Handle(object value) {
            TcpClient client = (TcpClient)value;
            SocketRequest request = null;
            try {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 1000; stream.WriteTimeout = 2000;
                MemoryStream input = new MemoryStream();
                Stopwatch clock = Stopwatch.StartNew();
                bool ended = false;
                while (clock.ElapsedMilliseconds < 5000) {
                    int b;
                    try { b = stream.ReadByte(); }
                    catch (IOException) { if (!running) return; continue; }
                    if (b < 0) return;
                    if (b == 10) { ended = true; break; }
                    if (input.Length >= 262144) { Send(stream, "{\"ok\":false,\"error\":\"request_too_large\"}"); return; }
                    input.WriteByte((byte)b);
                }
                if (!ended) { Send(stream, "{\"ok\":false,\"error\":\"read_timeout\"}"); return; }
                string json = new UTF8Encoding(false, true).GetString(input.ToArray()).TrimEnd('\r');
                request = new SocketRequest { Json = json, Deadline = DateTime.UtcNow.AddSeconds(20) };
                requests.Enqueue(request);
                if (!request.Completed.Wait(20000)) {
                    request.Cancelled = true;
                    Send(stream, "{\"ok\":false,\"error\":\"response_timeout\",\"message\":\"Do not retry open without checking the process.\"}");
                    return;
                }
                Send(stream, request.Response);
            } catch { }
            finally {
                if (request != null) request.Cancelled = true;
                byte ignored; clients.TryRemove(client, out ignored);
                client.Close(); slots.Release();
            }
        }
        private static void Send(NetworkStream stream, string json) {
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
        public void Dispose() {
            running = false; listener.Stop();
            foreach (TcpClient client in clients.Keys) client.Close();
            SocketRequest request;
            while (requests.TryDequeue(out request)) { request.Cancelled = true; request.Reply("{\"ok\":false,\"error\":\"server_stopped\"}"); }
            if (thread != null) thread.Join(1000);
        }
    }
}
