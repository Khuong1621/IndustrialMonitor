// ============================================================
// Helpers/Logger.cs
// Thể hiện: Singleton Pattern, Thread-safe logging
// ============================================================
using System;
using System.IO;
using System.Threading;

namespace IndustrialMonitor
{
    public enum LogLevel { Debug, Info, Warning, Error }

    /// <summary>
    /// Thread-safe Logger - Singleton Pattern
    /// Thể hiện: Singleton, double-check locking, thread safety
    /// </summary>
    public sealed class Logger
    {
        // --- Singleton (thread-safe, lazy) ---
        private static volatile Logger _instance;
        private static readonly object _syncRoot = new object();

        public static Logger Instance
        {
            get
            {
                if (_instance == null)               // First check (no lock)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)       // Second check (with lock)
                            _instance = new Logger();
                    }
                }
                return _instance;
            }
        }

        // --- Private constructor (Singleton) ---
        private Logger()
        {
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(_logPath);
        }

        // --- Fields ---
        private readonly string _logPath;
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
        public LogLevel MinLevel { get; set; } = LogLevel.Debug;
        public event EventHandler<(LogLevel Level, string Message)> LogAdded;

        /// <summary>
        /// Ghi log - thread-safe
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level < MinLevel) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string threadId = Thread.CurrentThread.ManagedThreadId.ToString("D3");
            string entry = $"[{timestamp}][T{threadId}][{level.ToString().ToUpper(),-7}] {message}";

            // Write to console
            Console.ForegroundColor = GetColor(level);
            Console.WriteLine(entry);
            Console.ResetColor();

            // Write to file - dùng ReaderWriterLock để nhiều thread đọc được đồng thời
            string logFile = Path.Combine(_logPath, $"{DateTime.Now:yyyy-MM-dd}.log");
            _rwLock.EnterWriteLock();
            try
            {
                File.AppendAllText(logFile, entry + Environment.NewLine);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }

            // Notify UI
            LogAdded?.Invoke(this, (level, entry));
        }

        private ConsoleColor GetColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug:   return ConsoleColor.Gray;
                case LogLevel.Warning: return ConsoleColor.Yellow;
                case LogLevel.Error:   return ConsoleColor.Red;
                default:               return ConsoleColor.White;
            }
        }
    }


    // ============================================================
    // Helpers/AppConfig.cs
    // ============================================================

    /// <summary>
    /// Cấu hình ứng dụng - đọc từ file config
    /// </summary>
    public class AppConfig
    {
        public string ServerHost { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 5000;
        public int PollingIntervalMs { get; set; } = 1000;
        public int WorkerThreadCount { get; set; } = 4;
        public string DefaultComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;

        private static AppConfig _current;
        public static AppConfig Current => _current ?? (_current = new AppConfig());
    }
}
