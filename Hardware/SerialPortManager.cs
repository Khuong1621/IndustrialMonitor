// ============================================================
// Hardware/SerialPortManager.cs
// Thể hiện: RS-232 / Serial Communication (COM port)
// ============================================================
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using IndustrialMonitor.Models;

namespace IndustrialMonitor.Hardware
{
    /// <summary>
    /// Quản lý giao tiếp RS-232 với thiết bị công nghiệp
    /// Hỗ trợ: RS-232, RS-485 (qua COM port), Modbus RTU (simplified)
    /// Thể hiện: SerialPort, Event-driven, Protocol parsing
    /// </summary>
    public class SerialPortManager : IDisposable
    {
        private SerialPort _port;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly object _writeLock = new object();

        // --- Configuration ---
        public string PortName { get; }
        public int BaudRate { get; }
        public bool IsOpen => _port?.IsOpen ?? false;

        // --- Events ---
        public event EventHandler<SensorData> DataReceived;
        public event EventHandler<string> RawDataReceived;
        public event EventHandler<string> ErrorOccurred;

        /// <summary>
        /// Constructor với cấu hình RS-232 tiêu chuẩn
        /// </summary>
        public SerialPortManager(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One)
        {
            PortName = portName;
            BaudRate = baudRate;

            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                Encoding = Encoding.ASCII,
                NewLine = "\r\n",           // Tùy thiết bị: \r\n hoặc \n
                ReadTimeout = 3000,         // 3 giây timeout
                WriteTimeout = 1000,
                DtrEnable = true,           // Data Terminal Ready
                RtsEnable = true            // Request To Send
            };

            _port.DataReceived += Port_DataReceived;
            _port.ErrorReceived += Port_ErrorReceived;
        }

        /// <summary>
        /// Mở cổng COM
        /// </summary>
        public bool Open()
        {
            try
            {
                if (!_port.IsOpen)
                    _port.Open();

                Logger.Instance.Log($"Serial port {PortName} opened @ {BaudRate} baud", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Cannot open {PortName}: {ex.Message}", LogLevel.Error);
                ErrorOccurred?.Invoke(this, ex.Message);
                return false;
            }
        }

        public void Close()
        {
            if (_port?.IsOpen == true)
                _port.Close();
        }

        // ---- RS-232 READ ----

        /// <summary>
        /// Event-driven: tự động gọi khi có dữ liệu đến
        /// Xử lý buffer để tránh mất dữ liệu khi data bị split
        /// </summary>
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string incoming = _port.ReadExisting();
                _buffer.Append(incoming);

                // Xử lý từng dòng hoàn chỉnh
                string bufStr = _buffer.ToString();
                int newlineIdx;

                while ((newlineIdx = bufStr.IndexOf('\n')) >= 0)
                {
                    string line = bufStr.Substring(0, newlineIdx).Trim();
                    _buffer.Remove(0, newlineIdx + 1);
                    bufStr = _buffer.ToString();

                    if (!string.IsNullOrEmpty(line))
                    {
                        RawDataReceived?.Invoke(this, line);

                        // Parse protocol
                        var data = ParseProtocol(line);
                        if (data != null) DataReceived?.Invoke(this, data);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Serial read error: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Parse dữ liệu theo nhiều định dạng phổ biến
        /// Hỗ trợ: CSV, Key=Value, Modbus-like
        /// </summary>
        private SensorData ParseProtocol(string rawLine)
        {
            // Format 1: CSV "1|Temperature|25.5|°C"
            if (rawLine.Contains("|"))
                return SensorData.Parse(rawLine);

            // Format 2: Key=Value "TEMP=25.5,HUM=60.2"
            if (rawLine.Contains("="))
                return ParseKeyValue(rawLine);

            // Format 3: Modbus-like hex "$01 04 00 01 00 01"
            if (rawLine.StartsWith("$"))
                return ParseModbusLike(rawLine);

            return null;
        }

        private SensorData ParseKeyValue(string line)
        {
            // "TEMP=25.5" → SensorData
            var parts = line.Split('=');
            if (parts.Length == 2 && double.TryParse(parts[1], out double val))
            {
                return new SensorData
                {
                    ParameterName = parts[0],
                    Value = val,
                    Timestamp = DateTime.Now,
                    IsValid = true
                };
            }
            return null;
        }

        private SensorData ParseModbusLike(string line)
        {
            // Simplified Modbus RTU parsing
            // Real Modbus cần CRC check, function codes...
            try
            {
                var parts = line.TrimStart('$').Split(' ');
                if (parts.Length >= 4)
                {
                    int deviceAddr = Convert.ToInt32(parts[0], 16);
                    int rawValue = (Convert.ToInt32(parts[3], 16) << 8)
                                  | Convert.ToInt32(parts[4], 16);
                    return new SensorData
                    {
                        DeviceId = deviceAddr,
                        ParameterName = "Register",
                        Value = rawValue,
                        Timestamp = DateTime.Now,
                        IsValid = true
                    };
                }
            }
            catch { }
            return null;
        }

        // ---- RS-232 WRITE ----

        /// <summary>
        /// Ghi lệnh xuống thiết bị
        /// lock để thread-safe khi nhiều thread cùng ghi
        /// </summary>
        public bool WriteCommand(string command)
        {
            lock (_writeLock)
            {
                try
                {
                    if (!_port.IsOpen) return false;
                    _port.WriteLine(command);
                    Logger.Instance.Log($"[{PortName}] SENT: {command}", LogLevel.Debug);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Serial write error: {ex.Message}", LogLevel.Error);
                    return false;
                }
            }
        }

        /// <summary>
        /// Ghi raw bytes (cho giao thức binary)
        /// </summary>
        public bool WriteBytes(byte[] data)
        {
            lock (_writeLock)
            {
                try
                {
                    _port.Write(data, 0, data.Length);
                    return true;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Request-Response: gửi lệnh và chờ phản hồi (polling mode)
        /// </summary>
        public string SendAndReceive(string command, int timeoutMs = 2000)
        {
            lock (_writeLock)
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.WriteLine(command);

                    // Chờ phản hồi
                    _port.ReadTimeout = timeoutMs;
                    return _port.ReadLine().Trim();
                }
                catch (TimeoutException)
                {
                    Logger.Instance.Log($"Timeout waiting response for: {command}", LogLevel.Warning);
                    return null;
                }
            }
        }

        private void Port_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            string msg = $"Serial error on {PortName}: {e.EventType}";
            Logger.Instance.Log(msg, LogLevel.Error);
            ErrorOccurred?.Invoke(this, msg);
        }

        /// <summary>
        /// Lấy danh sách COM port có sẵn trên máy
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Dispose()
        {
            Close();
            _port?.Dispose();
        }
    }
}
