using System;

namespace IndustrialMonitor.Models
{
    /// <summary>
    /// Mức độ cảnh báo
    /// </summary>
    public enum AlarmLevel
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// Model đại diện cho một cảnh báo / sự kiện trong hệ thống
    /// </summary>
    public class AlarmModel
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string Message { get; set; }
        public AlarmLevel Level { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsAcknowledged { get; set; }
        public string AcknowledgedBy { get; set; }
        public DateTime? AcknowledgedAt { get; set; }

        public AlarmModel() { }

        public AlarmModel(int deviceId, string message, AlarmLevel level)
        {
            DeviceId       = deviceId;
            Message        = message;
            Level          = level;
            CreatedAt      = DateTime.Now;
            IsAcknowledged = false;
        }

        /// <summary>Xác nhận đã xem alarm</summary>
        public void Acknowledge(string operator_)
        {
            IsAcknowledged  = true;
            AcknowledgedBy  = operator_;
            AcknowledgedAt  = DateTime.Now;
        }

        public override string ToString()
            => $"[{Level}] {CreatedAt:HH:mm:ss} - Device {DeviceId}: {Message}";
    }
}
