using System;

namespace IndustrialMonitor.Models
{
    /// <summary>
    /// DTO: Dữ liệu đọc được từ sensor, dùng để truyền giữa các tầng
    /// Pattern: Data Transfer Object
    /// </summary>
    public class SensorData
    {
        public int DeviceId { get; set; }
        public string ParameterName { get; set; }   // "Temperature", "Pressure", "RPM"
        public double Value { get; set; }
        public string Unit { get; set; }             // "°C", "Bar", "RPM"
        public DateTime Timestamp { get; set; }
        public bool IsValid { get; set; }

        public SensorData()
        {
            Timestamp = DateTime.Now;
            IsValid = true;
        }

        /// <summary>
        /// Parse từ raw string nhận qua Serial / TCP
        /// Format chuẩn: "DEVICE_ID|PARAM_NAME|VALUE|UNIT"
        /// Ví dụ: "1|Temperature|25.5|°C"
        /// </summary>
        public static SensorData Parse(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData))
                return new SensorData { IsValid = false };

            try
            {
                var parts = rawData.Trim().Split('|');
                if (parts.Length < 4)
                    return new SensorData { IsValid = false };

                return new SensorData
                {
                    DeviceId       = int.Parse(parts[0]),
                    ParameterName  = parts[1],
                    Value          = double.Parse(parts[2]),
                    Unit           = parts[3],
                    Timestamp      = DateTime.Now,
                    IsValid        = true
                };
            }
            catch
            {
                return new SensorData { IsValid = false };
            }
        }

        /// <summary>Serialize thành string để gửi qua mạng / serial</summary>
        public string Serialize()
            => $"{DeviceId}|{ParameterName}|{Value:F2}|{Unit}";

        public override string ToString()
            => $"[Device {DeviceId}] {ParameterName} = {Value} {Unit} @ {Timestamp:HH:mm:ss}";
    }
}
