using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace IndustrialMonitor.Core
{
    /// <summary>
    /// Quản lý các tiến trình con (child processes)
    /// Thể hiện: Multi-process, IPC qua stdin/stdout, Process lifecycle
    /// Ứng dụng thực tế: chạy data logger, report exporter, alarm notifier dưới dạng process riêng
    /// </summary>
    public class ProcessManager
    {
        private readonly Dictionary<string, Process> _processes = new Dictionary<string, Process>();
        private readonly object _processLock = new object();

        // --- Events ---
        public event EventHandler<string> ProcessOutput;
        public event EventHandler<(string Key, int ExitCode)> ProcessExited;

        // ---- LIFECYCLE ----

        /// <summary>
        /// Khởi chạy một tiến trình con
        /// </summary>
        /// <param name="key">Tên định danh (dùng để tham chiếu về sau)</param>
        /// <param name="exePath">Đường dẫn tới file exe</param>
        /// <param name="args">Tham số truyền vào</param>
        public bool StartProcess(string key, string exePath, string args = "")
        {
            lock (_processLock)
            {
                if (_processes.ContainsKey(key))
                {
                    Logger.Instance.Log($"Process '{key}' already running.", LogLevel.Warning);
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName               = exePath,
                    Arguments              = args,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput  = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                var process = new Process
                {
                    StartInfo            = psi,
                    EnableRaisingEvents  = true
                };

                // Đọc stdout không đồng bộ
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        ProcessOutput?.Invoke(this, $"[{key}] {e.Data}");
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        Logger.Instance.Log($"[{key}] STDERR: {e.Data}", LogLevel.Warning);
                };

                process.Exited += (s, e) =>
                {
                    int exitCode = process.ExitCode;
                    Logger.Instance.Log($"Process '{key}' exited with code {exitCode}.", LogLevel.Info);
                    ProcessExited?.Invoke(this, (key, exitCode));
                    lock (_processLock) { _processes.Remove(key); }
                };

                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    _processes[key] = process;
                    Logger.Instance.Log($"Process '{key}' started. PID={process.Id}", LogLevel.Info);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Failed to start '{key}': {ex.Message}", LogLevel.Error);
                    return false;
                }
            }
        }

        /// <summary>
        /// Gửi lệnh tới process con qua stdin (Inter-Process Communication)
        /// </summary>
        public void SendCommand(string key, string command)
        {
            lock (_processLock)
            {
                if (_processes.TryGetValue(key, out var proc))
                {
                    proc.StandardInput.WriteLine(command);
                    Logger.Instance.Log($"[{key}] CMD: {command}", LogLevel.Debug);
                }
                else
                {
                    Logger.Instance.Log($"Process '{key}' not found.", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// Dừng tiến trình con một cách graceful (cho thoát tự nhiên trước, kill sau)
        /// </summary>
        public void StopProcess(string key, int gracefulTimeoutMs = 3000)
        {
            lock (_processLock)
            {
                if (!_processes.TryGetValue(key, out var proc)) return;

                try
                {
                    // Gửi tín hiệu thoát
                    proc.StandardInput.WriteLine("EXIT");
                    bool exited = proc.WaitForExit(gracefulTimeoutMs);

                    if (!exited)
                    {
                        Logger.Instance.Log($"Force killing '{key}'...", LogLevel.Warning);
                        proc.Kill();
                    }

                    _processes.Remove(key);
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"StopProcess '{key}': {ex.Message}", LogLevel.Error);
                }
            }
        }

        public void StopAll()
        {
            // Lấy keys ra trước để tránh modify collection trong loop
            var keys = new List<string>(_processes.Keys);
            foreach (var key in keys)
                StopProcess(key);
        }

        // ---- INFO ----

        public bool IsRunning(string key)
        {
            lock (_processLock)
            {
                return _processes.TryGetValue(key, out var proc) && !proc.HasExited;
            }
        }

        public int ActiveProcessCount
        {
            get { lock (_processLock) { return _processes.Count; } }
        }

        /// <summary>
        /// Lấy memory usage của một process (MB)
        /// </summary>
        public double GetMemoryUsageMB(string key)
        {
            lock (_processLock)
            {
                if (_processes.TryGetValue(key, out var proc))
                    return proc.WorkingSet64 / 1024.0 / 1024.0;
            }
            return 0;
        }
    }
}
