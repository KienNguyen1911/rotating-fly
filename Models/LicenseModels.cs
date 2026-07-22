using System;
using System.Text.Json.Serialization;

namespace AssetAutomator.Models
{
    public class LicenseRequest
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("licenseKey")]
        public string LicenseKey { get; set; } = string.Empty;

        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("newDeviceId")]
        public string? NewDeviceId { get; set; }

        [JsonPropertyName("appId")]
        public string AppId { get; set; } = "AssetAutomator";
    }

    public class LicenseResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("activatedAt")]
        public string? ActivatedAt { get; set; }

        [JsonPropertyName("expiredAt")]
        public string? ExpiredAt { get; set; }

        [JsonPropertyName("remainingDays")]
        public int RemainingDays { get; set; }
    }

    public class LocalLicenseToken
    {
        public string LicenseKey { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public DateTime ExpiredAt { get; set; }
        public DateTime LastVerifiedOnline { get; set; }
        public string Status { get; set; } = "Active";

        public bool IsValidOffline(string currentDeviceId)
        {
            if (string.IsNullOrWhiteSpace(LicenseKey) || string.IsNullOrWhiteSpace(DeviceId))
                return false;

            if (DeviceId != currentDeviceId)
                return false;

            DateTime now = DateTime.UtcNow;

            // 1. License expiration check
            if (now > ExpiredAt)
                return false;

            // 2. Offline grace period: maximum 7 days since last online verification
            if ((now - LastVerifiedOnline).TotalDays > 7)
                return false;

            return true;
        }
    }
}
