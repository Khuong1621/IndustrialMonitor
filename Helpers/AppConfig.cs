using System;
using System.IO;
using System.Xml.Linq;

namespace IndustrialMonitor.Helpers
{
    /// <summary>
    /// Cấu hình toàn bộ ứng dụng — đọc/ghi từ file XML config
    /// Thể hiện: Configuration management, XML I/O
    /// </summary>
    public class AppConfig
    {
        // --- Network ---
        public string ServerHost       { get; set; } = "127.0.0.1";
        public int    ServerPort       { get; set; } = 5000;
        public int    ReconnectDelayMs { get; set; } = 5000;

        // --- Polling ---
        public int PollingIntervalMs   { get; set; } = 1000;

        // --- Threading ---
        public int WorkerThreadCount   { get; set; } = 4;

        // --- Serial / RS-232 ---
        public string DefaultComPort   { get; set; } = "COM1";
        public int    BaudRate         { get; set; } = 9600;

        // --- Logging ---
        public string LogDirectory     { get; set; } = "logs";
        public string MinLogLevel      { get; set; } = "Info";

        // ---- Singleton ----
        private static AppConfig _current;
        private static readonly object _lock = new object();

        public static AppConfig Current
        {
            get
            {
                if (_current == null)
                    lock (_lock)
                        if (_current == null)
                            _current = Load();
                return _current;
            }
        }

        // ---- Persistence ----

        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppConfig.xml");

        /// <summary>Đọc config từ XML, tạo mới nếu chưa có</summary>
        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath))
            {
                var defaultCfg = new AppConfig();
                defaultCfg.Save();
                return defaultCfg;
            }

            try
            {
                var xml = XDocument.Load(ConfigPath);
                var root = xml.Root;

                string Get(string name, string def)
                    => root.Element(name)?.Value ?? def;

                return new AppConfig
                {
                    ServerHost        = Get("ServerHost",       "127.0.0.1"),
                    ServerPort        = int.Parse(Get("ServerPort",       "5000")),
                    ReconnectDelayMs  = int.Parse(Get("ReconnectDelayMs", "5000")),
                    PollingIntervalMs = int.Parse(Get("PollingIntervalMs","1000")),
                    WorkerThreadCount = int.Parse(Get("WorkerThreadCount","4")),
                    DefaultComPort    = Get("DefaultComPort",   "COM1"),
                    BaudRate          = int.Parse(Get("BaudRate",         "9600")),
                    LogDirectory      = Get("LogDirectory",     "logs"),
                    MinLogLevel       = Get("MinLogLevel",      "Info")
                };
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Config load error: {ex.Message}. Using defaults.", LogLevel.Warning);
                return new AppConfig();
            }
        }

        /// <summary>Lưu config ra XML</summary>
        public void Save()
        {
            try
            {
                var xml = new XDocument(
                    new XElement("AppConfig",
                        new XElement("ServerHost",       ServerHost),
                        new XElement("ServerPort",       ServerPort),
                        new XElement("ReconnectDelayMs", ReconnectDelayMs),
                        new XElement("PollingIntervalMs",PollingIntervalMs),
                        new XElement("WorkerThreadCount",WorkerThreadCount),
                        new XElement("DefaultComPort",   DefaultComPort),
                        new XElement("BaudRate",         BaudRate),
                        new XElement("LogDirectory",     LogDirectory),
                        new XElement("MinLogLevel",      MinLogLevel)
                    )
                );
                xml.Save(ConfigPath);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Config save error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
