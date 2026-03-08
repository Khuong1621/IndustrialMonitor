using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace IndustrialMonitor.Core
{
    /// <summary>
    /// Thread Pool tự triển khai để xử lý các tác vụ nền
    /// Thể hiện: Producer-Consumer pattern, BlockingCollection, Thread management
    /// Ứng dụng: xử lý alarm, ghi database, gửi email notification...
    /// </summary>
    public class WorkerPool : IDisposable
    {
        private readonly BlockingCollection<Action> _taskQueue;
        private readonly List<Thread> _workers;
        private bool _disposed = false;

        public int WorkerCount { get; }
        public int PendingTasks => _taskQueue.Count;

        /// <summary>
        /// Khởi tạo pool với số lượng worker threads cố định
        /// </summary>
        /// <param name="workerCount">Số thread xử lý song song</param>
        /// <param name="maxQueueSize">Giới hạn hàng đợi (0 = không giới hạn)</param>
        public WorkerPool(int workerCount = 4, int maxQueueSize = 200)
        {
            WorkerCount = workerCount;
            _taskQueue  = maxQueueSize > 0
                ? new BlockingCollection<Action>(maxQueueSize)
                : new BlockingCollection<Action>();

            _workers = new List<Thread>(workerCount);

            for (int i = 0; i < workerCount; i++)
            {
                var worker = new Thread(WorkerLoop)
                {
                    Name         = $"WorkerPool_{i}",
                    IsBackground = true,
                    Priority     = ThreadPriority.BelowNormal
                };
                _workers.Add(worker);
                worker.Start();
            }

            Logger.Instance.Log($"WorkerPool started with {workerCount} threads.", LogLevel.Info);
        }

        /// <summary>
        /// Đẩy task vào queue (Producer)
        /// Thread-safe, không block nếu queue đầy (TryAdd với timeout)
        /// </summary>
        public bool Enqueue(Action task)
        {
            if (_disposed || task == null) return false;

            bool added = _taskQueue.TryAdd(task, millisecondsTimeout: 50);
            if (!added)
                Logger.Instance.Log("WorkerPool queue full, task dropped.", LogLevel.Warning);

            return added;
        }

        /// <summary>
        /// Worker loop - Consumer
        /// Mỗi thread chạy loop này, lấy task từ queue và thực thi
        /// </summary>
        private void WorkerLoop()
        {
            // GetConsumingEnumerable: block khi queue trống, tự thoát khi CompleteAdding()
            foreach (var task in _taskQueue.GetConsumingEnumerable())
            {
                try
                {
                    task();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"[{Thread.CurrentThread.Name}] Task error: {ex.Message}", LogLevel.Error);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _taskQueue.CompleteAdding();  // Báo workers không có thêm task

            foreach (var w in _workers)
                w.Join(2000);  // Chờ worker xử lý hết

            _taskQueue.Dispose();
            Logger.Instance.Log("WorkerPool disposed.", LogLevel.Info);
        }
    }
}
