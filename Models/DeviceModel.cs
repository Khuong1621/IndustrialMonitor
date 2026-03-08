using System;

namespace IndustrialMonitor.Models
{
    /// <summary>
    /// Loại thiết bị công nghiệp
    /// </summary>
    public enum DeviceType
    {
        Sensor,
        PLC,
        HMI,
        Actuator
    }

    /// <summary>
    /// Trạng thái kết nối của thiết bị
    /// </summary>
    public enum DeviceStatus
    {
        Offline,
        Online,
        Warning,
        Error
    }

    /// <summary>
    /// Model đại diện cho một thiết bị công nghiệp
    /// Thể hiện: Encapsulation, Properties
    /// </summary>
    public class DeviceModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DeviceType Type { get; set; }
        public DeviceStatus Status { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string ComPort { get; set; }      // RS-232: COM1, COM2...
        public DateTime LastSeen { get; set; }
        public string Location { get; set; }

        public DeviceModel()
        {
            Status = DeviceStatus.Offline;
            LastSeen = DateTime.Now;
        }

        public override string ToString()
            => $"[{Id}] {Name} ({Type}) - {Status} @ {IpAddress}:{Port}";
    }
}
