// ============================================================
// Network/TcpServer.cs
// Thể hiện: TCP/IP Socket - Server side, async accept
// ============================================================
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using IndustrialMonitor.Models;

namespace IndustrialMonitor.Network
{
    /// <summary>
    /// TCP Server lắng nghe kết nối từ các thiết bị/client
    /// Thể hiện: TCP/IP Socket Server, async I/O, multi-client handling
    /// </summary>
    public class TcpServer : IDisposable
    {
        private TcpListener _listener;
        private Thread _acceptThread;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, TcpClient> _clients
            = new ConcurrentDictionary<string, TcpClient>();

        public int Port { get; }
        public bool IsRunning { get; private set; }

        // Events
        public event EventHandler<string> ClientConnected;
        public event EventHandler<string> ClientDisconnected;
        public event EventHandler<(string ClientId, SensorData Data)> DataReceived;
        public event EventHandler<string> MessageReceived;

        public TcpServer(int port = 5000)
        {
            Port = port;
        }

        /// <summary>
        /// Bắt đầu lắng nghe kết nối
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            IsRunning = true;

            // Thread chính: accept connections
            _acceptThread = new Thread(AcceptLoop)
            {
                Name = "TCP_AcceptThread",
                IsBackground = true
            };
            _acceptThread.Start();

            Logger.Instance.Log($"TCP Server started on port {Port}", LogLevel.Info);
        }

        /// <summary>
        /// Loop chờ kết nối từ client
        /// </summary>
        private void AcceptLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Blocking accept - chờ client kết nối
                    var client = _listener.AcceptTcpClient();
                    string clientId = client.Client.RemoteEndPoint.ToString();

                    _clients[clientId] = client;
                    ClientConnected?.Invoke(this, clientId);
                    Logger.Instance.Log($"Client connected: {clientId}", LogLevel.Info);

                    // Mỗi client có 1 thread riêng để đọc dữ liệu
                    var clientThread = new Thread(() => HandleClient(client, clientId))
                    {
                        Name = $"TCP_Client_{clientId}",
                        IsBackground = true
                    };
                    clientThread.Start();
                }
                catch (SocketException) when (_cts.Token.IsCancellationRequested)
                {
                    break; // Server đã dừng
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Accept error: {ex.Message}", LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Xử lý dữ liệu từ một client cụ thể
        /// </summary>
        private void HandleClient(TcpClient client, string clientId)
        {
            try
            {
                var stream = client.GetStream();
                byte[] buffer = new byte[4096];

                while (client.Connected && !_cts.Token.IsCancellationRequested)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Client disconnect

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    MessageReceived?.Invoke(this, message);

                    // Parse thành SensorData
                    var data = SensorData.Parse(message);
                    if (data.IsValid)
                        DataReceived?.Invoke(this, (clientId, data));
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Client {clientId} error: {ex.Message}", LogLevel.Warning);
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                client.Close();
                ClientDisconnected?.Invoke(this, clientId);
                Logger.Instance.Log($"Client disconnected: {clientId}", LogLevel.Info);
            }
        }

        /// <summary>
        /// Broadcast message đến tất cả client
        /// </summary>
        public void Broadcast(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            foreach (var kvp in _clients)
            {
                try
                {
                    kvp.Value.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    _clients.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Gửi message đến client cụ thể
        /// </summary>
        public bool SendTo(string clientId, string message)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    client.GetStream().Write(data, 0, data.Length);
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        public int ConnectedClients => _clients.Count;

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            foreach (var client in _clients.Values) client.Close();
            _clients.Clear();
            IsRunning = false;
            Logger.Instance.Log("TCP Server stopped.", LogLevel.Info);
        }

        public void Dispose() => Stop();
    }


    // ============================================================
    // Network/TcpClientManager.cs
    // Thể hiện: TCP/IP Client - reconnect logic, heartbeat
    // ============================================================

    /// <summary>
    /// TCP Client với auto-reconnect và heartbeat
    /// Thể hiện: Client-side TCP, Reconnect pattern, Heartbeat
    /// </summary>
    public class TcpClientManager : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private Thread _heartbeatThread;
        private CancellationTokenSource _cts;

        private readonly string _host;
        private readonly int _port;
        private readonly int _reconnectDelayMs;

        public bool IsConnected => _client?.Connected ?? false;

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<string> MessageReceived;
        public event EventHandler<SensorData> DataReceived;

        public TcpClientManager(string host, int port, int reconnectDelayMs = 5000)
        {
            _host = host;
            _port = port;
            _reconnectDelayMs = reconnectDelayMs;
        }

        public void Connect()
        {
            _cts = new CancellationTokenSource();

            var connectThread = new Thread(ConnectLoop)
            {
                IsBackground = true,
                Name = "TCP_ConnectLoop"
            };
            connectThread.Start();
        }

        /// <summary>
        /// Auto-reconnect loop
        /// </summary>
        private void ConnectLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _client = new TcpClient();
                    _client.Connect(_host, _port);
                    _stream = _client.GetStream();

                    Connected?.Invoke(this, EventArgs.Empty);
                    Logger.Instance.Log($"Connected to {_host}:{_port}", LogLevel.Info);

                    // Start receive & heartbeat threads
                    StartReceiveThread();
                    StartHeartbeatThread();

                    // Chờ đến khi mất kết nối
                    while (_client.Connected && !_cts.Token.IsCancellationRequested)
                        Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Connection failed: {ex.Message}. Retrying...", LogLevel.Warning);
                }
                finally
                {
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    _client?.Close();
                }

                // Chờ trước khi reconnect
                if (!_cts.Token.IsCancellationRequested)
                    _cts.Token.WaitHandle.WaitOne(_reconnectDelayMs);
            }
        }

        private void StartReceiveThread()
        {
            _receiveThread = new Thread(() =>
            {
                byte[] buffer = new byte[4096];
                while (_client.Connected && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        int n = _stream.Read(buffer, 0, buffer.Length);
                        if (n == 0) break;

                        string msg = Encoding.UTF8.GetString(buffer, 0, n).Trim();
                        MessageReceived?.Invoke(this, msg);

                        var data = SensorData.Parse(msg);
                        if (data.IsValid) DataReceived?.Invoke(this, data);
                    }
                    catch { break; }
                }
            })
            { IsBackground = true, Name = "TCP_Receive" };
            _receiveThread.Start();
        }

        /// <summary>
        /// Heartbeat: gửi ping định kỳ để giữ kết nối
        /// </summary>
        private void StartHeartbeatThread()
        {
            _heartbeatThread = new Thread(() =>
            {
                while (_client.Connected && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        Send("PING");
                        _cts.Token.WaitHandle.WaitOne(30000); // 30s
                    }
                    catch { break; }
                }
            })
            { IsBackground = true, Name = "TCP_Heartbeat" };
            _heartbeatThread.Start();
        }

        public bool Send(string message)
        {
            if (!IsConnected || _stream == null) return false;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                _stream.Write(data, 0, data.Length);
                return true;
            }
            catch { return false; }
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _client?.Close();
        }

        public void Dispose() => Disconnect();
    }
}
