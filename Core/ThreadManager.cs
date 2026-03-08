// ============================================================
// Core/ThreadManager.cs
// Thể hiện: Multi-threading, Thread Safety, CancellationToken
// ============================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using IndustrialMonitor.Models;

namespace IndustrialMonitor.Core
{
    /// <summary>
    /// Quản lý các thread cho việc polling dữ liệu từ nhiều thiết bị đồng thời
    /// Thể hiện: Multi-threading, Thread Safety với lock, CancellationToken
    /// </summary>
    public class ThreadManager : IDisposable
    {
        // --- Thread-safe dictionary để lưu thread cho từng thiết bị ---
        private readonly Dictionary<int, Thread> _deviceThreads = new Dictionary<int, Thread>();
        private readonly Dictionary<int, CancellationTokenSource> _cancellationTokens
            = new Dictionary<int, CancellationTokenSource>();

        // --- lock object để bảo vệ shared resources ---
        private readonly object _lock = new object();

        // --- Thread-safe queue cho dữ liệu nhận được ---
        private readonly ConcurrentQueue<SensorData> _dataQueue = new ConcurrentQueue<SensorData>();

        // --- Event để notify UI thread ---
        public event EventHandler<SensorData> DataQueued;

        /// <summary>
        /// Bắt đầu một thread polling cho thiết bị
        /// </summary>
        public void StartPolling(BaseDevice device, int intervalMs = 1000)
        {
            int deviceId = device.Model.Id;

            lock (_lock)
            {
                // Dừng thread cũ nếu đã tồn tại
                StopPolling(deviceId);

                var cts = new CancellationTokenSource();
                var token = cts.Token;

                var thread = new Thread(() =>
                {
                    Logger.Instance.Log($"Polling thread started for Device {deviceId}", LogLevel.Info);

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (device.IsConnected)
                            {
                                var data = device.ReadData();
                                if (data != null && data.IsValid)
                                {
                                    // Enqueue thread-safe
                                    _dataQueue.Enqueue(data);
                                    DataQueued?.Invoke(this, data);
                                }
                            }

                            // Interruptible sleep: phản hồi nhanh khi cancel
                            token.WaitHandle.WaitOne(intervalMs);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"Polling error Device {deviceId}: {ex.Message}", LogLevel.Error);
                            Thread.Sleep(5000); // Back-off khi có lỗi
                        }
                    }

                    Logger.Instance.Log($"Polling thread stopped for Device {deviceId}", LogLevel.Info);
                });

                thread.Name = $"DevicePolling_{deviceId}";
                thread.IsBackground = true;    // Background thread: tự kết thúc khi app đóng
                thread.Priority = ThreadPriority.BelowNormal;

                _deviceThreads[deviceId] = thread;
                _cancellationTokens[deviceId] = cts;

                thread.Start();
            }
        }

        /// <summary>
        /// Dừng polling cho một thiết bị cụ thể
        /// </summary>
        public void StopPolling(int deviceId)
        {
            lock (_lock)
            {
                if (_cancellationTokens.TryGetValue(deviceId, out var cts))
                {
                    cts.Cancel();
                    _cancellationTokens.Remove(deviceId);
                }

                if (_deviceThreads.TryGetValue(deviceId, out var thread))
                {
                    thread.Join(3000);  // Chờ tối đa 3s
                    _deviceThreads.Remove(deviceId);
                }
            }
        }

        /// <summary>
        /// Lấy dữ liệu từ queue (gọi từ UI thread)
        /// </summary>
        public bool TryDequeue(out SensorData data)
        {
            return _dataQueue.TryDequeue(out data);
        }

        public int ActiveThreadCount
        {
            get { lock (_lock) { return _deviceThreads.Count; } }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var cts in _cancellationTokens.Values)
                    cts.Cancel();

                foreach (var thread in _deviceThreads.Values)
                    thread.Join(1000);

                _deviceThreads.Clear();
                _cancellationTokens.Clear();
            }
        }
    }

}
