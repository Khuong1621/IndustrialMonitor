// ============================================================
// Core/BaseDevice.cs
// Thể hiện: Abstract Class, Interface, Inheritance, OOP
// ============================================================
using System;
using IndustrialMonitor.Models;

namespace IndustrialMonitor.Core
{
    // ---- INTERFACES ----

    /// <summary>
    /// Interface cho mọi thiết bị có thể kết nối
    /// Thể hiện: Interface Segregation Principle (SOLID)
    /// </summary>
    public interface IConnectable
    {
        bool Connect();
        void Disconnect();
        bool IsConnected { get; }
    }

    /// <summary>
    /// Interface cho thiết bị có thể đọc dữ liệu
    /// </summary>
    public interface IDataReader
    {
        SensorData ReadData();
        event EventHandler<SensorData> DataReceived;
    }

    /// <summary>
    /// Interface cho thiết bị có thể gửi lệnh
    /// </summary>
    public interface ICommandSender
    {
        bool SendCommand(string command);
    }

    // ---- ABSTRACT BASE CLASS ----

    /// <summary>
    /// Lớp cơ sở trừu tượng cho tất cả thiết bị công nghiệp
    /// Thể hiện: Abstract Class, Template Method Pattern
    /// </summary>
    public abstract class BaseDevice : IConnectable, IDataReader, ICommandSender
    {
        // --- Fields ---
        protected DeviceModel _model;
        protected bool _isConnected;
        private static int _instanceCount = 0;  // Static field - shared across instances

        // --- Events ---
        public event EventHandler<SensorData> DataReceived;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<AlarmModel> AlarmTriggered;

        // --- Properties ---
        public bool IsConnected => _isConnected;
        public DeviceModel Model => _model;
        public static int TotalDevices => _instanceCount;

        // --- Constructor ---
        protected BaseDevice(DeviceModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _instanceCount++;
        }

        // --- Abstract Methods (bắt buộc override ở subclass) ---
        protected abstract bool OpenConnection();
        protected abstract void CloseConnection();
        protected abstract SensorData ReadFromHardware();

        // --- Template Method Pattern ---
        /// <summary>
        /// Template method: định nghĩa skeleton, subclass fill chi tiết
        /// </summary>
        public bool Connect()
        {
            if (_isConnected) return true;

            Logger.Instance.Log($"Connecting to {_model.Name}...", LogLevel.Info);

            bool result = OpenConnection();   // Gọi abstract method
            if (result)
            {
                _isConnected = true;
                _model.Status = DeviceStatus.Online;
                _model.LastSeen = DateTime.Now;
                OnStatusChanged("Connected");
            }
            else
            {
                _model.Status = DeviceStatus.Error;
                OnStatusChanged("Connection Failed");
            }

            return result;
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            CloseConnection();
            _isConnected = false;
            _model.Status = DeviceStatus.Offline;
            OnStatusChanged("Disconnected");
        }

        public SensorData ReadData()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Device is not connected.");

            var data = ReadFromHardware();
            if (data != null && data.IsValid)
                OnDataReceived(data);

            return data;
        }

        public abstract bool SendCommand(string command);

        // --- Protected Event Raisers ---
        protected void OnDataReceived(SensorData data)
        {
            DataReceived?.Invoke(this, data);
        }

        protected void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        protected void OnAlarmTriggered(AlarmModel alarm)
        {
            AlarmTriggered?.Invoke(this, alarm);
        }

        // --- Virtual method (có thể override) ---
        public virtual string GetDeviceInfo()
        {
            return $"Device: {_model.Name}, Type: {_model.Type}, Status: {_model.Status}";
        }
    }

    // ---- CONCRETE IMPLEMENTATIONS ----

    /// <summary>
    /// Thiết bị kết nối qua TCP/IP
    /// Thể hiện: Inheritance từ BaseDevice
    /// </summary>
    public class TcpDevice : BaseDevice
    {
        private System.Net.Sockets.TcpClient _tcpClient;

        public TcpDevice(DeviceModel model) : base(model) { }

        protected override bool OpenConnection()
        {
            try
            {
                _tcpClient = new System.Net.Sockets.TcpClient();
                _tcpClient.Connect(_model.IpAddress, _model.Port);
                return _tcpClient.Connected;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"TCP Connect error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        protected override void CloseConnection()
        {
            _tcpClient?.Close();
            _tcpClient = null;
        }

        protected override SensorData ReadFromHardware()
        {
            try
            {
                var stream = _tcpClient.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string raw = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                return SensorData.Parse(raw);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"TCP Read error: {ex.Message}", LogLevel.Error);
                return new SensorData { IsValid = false };
            }
        }

        public override bool SendCommand(string command)
        {
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(command);
                _tcpClient.GetStream().Write(data, 0, data.Length);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Thiết bị kết nối qua RS-232 Serial
    /// Thể hiện: Inheritance, override abstract methods
    /// </summary>
    public class SerialDevice : BaseDevice
    {
        private System.IO.Ports.SerialPort _serialPort;

        public SerialDevice(DeviceModel model) : base(model) { }

        protected override bool OpenConnection()
        {
            try
            {
                _serialPort = new System.IO.Ports.SerialPort(
                    _model.ComPort, 9600,
                    System.IO.Ports.Parity.None, 8,
                    System.IO.Ports.StopBits.One);

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                return _serialPort.IsOpen;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Serial Connect error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private void SerialPort_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            string raw = _serialPort.ReadLine();
            var data = SensorData.Parse(raw);
            if (data.IsValid) OnDataReceived(data);
        }

        protected override void CloseConnection()
        {
            if (_serialPort?.IsOpen == true)
                _serialPort.Close();
        }

        protected override SensorData ReadFromHardware()
        {
            // Serial dùng event-driven (SerialPort_DataReceived), không cần poll
            return null;
        }

        public override bool SendCommand(string command)
        {
            try
            {
                _serialPort.WriteLine(command);
                return true;
            }
            catch { return false; }
        }

        public override string GetDeviceInfo()
        {
            // Override để thêm COM port info
            return base.GetDeviceInfo() + $", COM: {_model.ComPort}";
        }
    }
}
