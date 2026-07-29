using System;
using System.Management;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AssetAutomator.Infrastructure.Helpers
{
    /// <summary>
    /// Generates a unique hardware DeviceId based on SHA256(CPU + Mainboard + Disk Serial + Windows SID).
    /// </summary>
    public static class DeviceHelper
    {
        private static string? _cachedDeviceId;

        public static string GetDeviceId()
        {
            if (!string.IsNullOrEmpty(_cachedDeviceId))
            {
                return _cachedDeviceId;
            }

            try
            {
                string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
                string motherboardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
                string diskSerial = GetWmiProperty("Win32_DiskDrive", "SerialNumber");
                string userSid = GetWindowsUserSid();

                string rawHardwareId = $"CPU:{cpuId}|MB:{motherboardId}|DISK:{diskSerial}|SID:{userSid}";
                _cachedDeviceId = ComputeSha256(rawHardwareId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceHelper] Error building DeviceId: {ex.Message}");
                string fallback = $"FALLBACK|{Environment.MachineName}|{GetWindowsUserSid()}";
                _cachedDeviceId = ComputeSha256(fallback);
            }

            return _cachedDeviceId;
        }

        private static string GetWmiProperty(string wmiClass, string propertyName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}");
                using var collection = searcher.Get();
                foreach (var obj in collection)
                {
                    var val = obj[propertyName]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceHelper] WMI Query {wmiClass}.{propertyName} failed: {ex.Message}");
            }
            return "UNKNOWN";
        }

        private static string GetWindowsUserSid()
        {
            try
            {
                var currentIdentity = WindowsIdentity.GetCurrent();
                return currentIdentity.User?.Value ?? "UNKNOWN_SID";
            }
            catch
            {
                return "UNKNOWN_SID";
            }
        }

        private static string ComputeSha256(string rawData)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
