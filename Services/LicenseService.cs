using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Helpers;
using AssetAutomator.Models;

namespace AssetAutomator.Services
{
    public class LicenseService : IDisposable
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        private static readonly string TokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "AssetAutomator", 
            "license.dat"
        );

        private System.Threading.Timer? _heartbeatTimer;
        private bool _isHeartbeatRunning;

        public event EventHandler<string>? OnSessionInvalidated;

        public LocalLicenseToken? CurrentToken { get; private set; }

        public LicenseService()
        {
            CurrentToken = LoadLocalToken();
        }

        public string GetDeviceId()
        {
            return DeviceHelper.GetDeviceId();
        }

        public async Task<LicenseResponse> ActivateAsync(string licenseKey, string? serverUrl = null)
        {
            string url = GetServerUrl(serverUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                return new LicenseResponse { Success = false, Message = "Chưa cấu hình URL License Server." };
            }

            string deviceId = GetDeviceId();
            var req = new LicenseRequest
            {
                Action = "activate",
                LicenseKey = licenseKey.Trim(),
                DeviceId = deviceId,
                AppId = "AssetAutomator"
            };

            var resp = await PostApiAsync(url, req);
            if (resp.Success && !string.IsNullOrEmpty(resp.SessionId))
            {
                var token = new LocalLicenseToken
                {
                    LicenseKey = licenseKey.Trim(),
                    DeviceId = deviceId,
                    SessionId = resp.SessionId,
                    ExpiredAt = DateTime.TryParse(resp.ExpiredAt, out var exp) ? exp : DateTime.UtcNow.AddDays(30),
                    LastVerifiedOnline = DateTime.UtcNow,
                    Status = resp.Status
                };

                await SaveLocalTokenAsync(token);
                CurrentToken = token;

                // Save to appsettings
                ConfigService.CurrentSettings.LicenseKey = licenseKey.Trim();
                ConfigService.CurrentSettings.LicenseServerUrl = url;
                ConfigService.SaveSettings(ConfigService.CurrentSettings);

                StartHeartbeatTimer();
            }

            return resp;
        }

        public async Task<LicenseResponse> VerifyAsync(string? serverUrl = null)
        {
            string url = GetServerUrl(serverUrl);
            string deviceId = GetDeviceId();

            // Load token if not cached
            CurrentToken ??= LoadLocalToken();

            if (CurrentToken == null || string.IsNullOrWhiteSpace(CurrentToken.LicenseKey))
            {
                return new LicenseResponse 
                { 
                    Success = false, 
                    Code = "NO_LICENSE", 
                    Message = "Chưa tìm thấy bản quyền trên thiết bị này." 
                };
            }

            // Attempt online verification
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    var req = new LicenseRequest
                    {
                        Action = "verify",
                        LicenseKey = CurrentToken.LicenseKey,
                        DeviceId = deviceId,
                        SessionId = CurrentToken.SessionId
                    };

                    var resp = await PostApiAsync(url, req);
                    if (resp.Success)
                    {
                        CurrentToken.LastVerifiedOnline = DateTime.UtcNow;
                        if (DateTime.TryParse(resp.ExpiredAt, out var exp))
                        {
                            CurrentToken.ExpiredAt = exp;
                        }
                        await SaveLocalTokenAsync(CurrentToken);
                        StartHeartbeatTimer();
                        return resp;
                    }
                    else if (resp.Code == "INVALID_SESSION" || resp.Code == "REVOKED" || resp.Code == "EXPIRED")
                    {
                        // Invalidated on server
                        await DeleteLocalTokenAsync();
                        CurrentToken = null;
                        return resp;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LicenseService] Online verify failed: {ex.Message}. Falling back to offline token check.");
                }
            }

            // Fallback: Offline verification (7-day grace period)
            if (CurrentToken != null && CurrentToken.IsValidOffline(deviceId))
            {
                int remainingDays = Math.Max(0, (int)(CurrentToken.ExpiredAt - DateTime.UtcNow).TotalDays);
                return new LicenseResponse
                {
                    Success = true,
                    Status = CurrentToken.Status,
                    SessionId = CurrentToken.SessionId,
                    ExpiredAt = CurrentToken.ExpiredAt.ToString("o"),
                    RemainingDays = remainingDays,
                    Message = $"Xác thực offline thành công (Còn {remainingDays} ngày hiệu lực)."
                };
            }

            return new LicenseResponse
            {
                Success = false,
                Code = "OFFLINE_EXPIRED",
                Message = "Xác thực offline đã quá thời hạn 7 ngày hoặc bản quyền đã hết hạn. Vui lòng kết nối mạng để xác thực lại."
            };
        }

        public async Task<LicenseResponse> SendHeartbeatAsync()
        {
            if (CurrentToken == null || string.IsNullOrWhiteSpace(CurrentToken.LicenseKey))
            {
                return new LicenseResponse { Success = false, Code = "NO_TOKEN", Message = "No local token" };
            }

            string url = GetServerUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return new LicenseResponse { Success = false, Message = "No server URL" };
            }

            var req = new LicenseRequest
            {
                Action = "heartbeat",
                LicenseKey = CurrentToken.LicenseKey,
                DeviceId = GetDeviceId(),
                SessionId = CurrentToken.SessionId
            };

            var resp = await PostApiAsync(url, req);
            if (resp.Success)
            {
                CurrentToken.LastVerifiedOnline = DateTime.UtcNow;
                await SaveLocalTokenAsync(CurrentToken);
            }
            else if (resp.Code == "INVALID_SESSION" || resp.Code == "REVOKED" || resp.Code == "EXPIRED")
            {
                StopHeartbeatTimer();
                await DeleteLocalTokenAsync();
                CurrentToken = null;

                OnSessionInvalidated?.Invoke(this, resp.Message ?? "Session bản quyền của bạn đã hết hạn hoặc được kích hoạt ở máy khác.");
            }

            return resp;
        }

        public async Task<LicenseResponse> DeactivateAsync()
        {
            if (CurrentToken == null || string.IsNullOrWhiteSpace(CurrentToken.LicenseKey))
            {
                return new LicenseResponse { Success = false, Message = "Không tìm thấy token để hủy." };
            }

            string url = GetServerUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return new LicenseResponse { Success = false, Message = "Chưa cấu hình URL License Server." };
            }

            var req = new LicenseRequest
            {
                Action = "deactivate",
                LicenseKey = CurrentToken.LicenseKey,
                DeviceId = GetDeviceId()
            };

            var resp = await PostApiAsync(url, req);
            if (resp.Success)
            {
                StopHeartbeatTimer();
                await DeleteLocalTokenAsync();
                CurrentToken = null;
            }

            return resp;
        }

        public async Task<LicenseResponse> TransferAsync(string licenseKey, string? serverUrl = null)
        {
            string url = GetServerUrl(serverUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                return new LicenseResponse { Success = false, Message = "Chưa cấu hình URL License Server." };
            }

            string newDeviceId = GetDeviceId();
            var req = new LicenseRequest
            {
                Action = "transfer",
                LicenseKey = licenseKey.Trim(),
                NewDeviceId = newDeviceId
            };

            var resp = await PostApiAsync(url, req);
            if (resp.Success && !string.IsNullOrEmpty(resp.SessionId))
            {
                var token = new LocalLicenseToken
                {
                    LicenseKey = licenseKey.Trim(),
                    DeviceId = newDeviceId,
                    SessionId = resp.SessionId,
                    ExpiredAt = DateTime.TryParse(resp.ExpiredAt, out var exp) ? exp : DateTime.UtcNow.AddDays(30),
                    LastVerifiedOnline = DateTime.UtcNow,
                    Status = resp.Status
                };

                await SaveLocalTokenAsync(token);
                CurrentToken = token;

                ConfigService.CurrentSettings.LicenseKey = licenseKey.Trim();
                ConfigService.CurrentSettings.LicenseServerUrl = url;
                ConfigService.SaveSettings(ConfigService.CurrentSettings);

                StartHeartbeatTimer();
            }

            return resp;
        }

        public void StartHeartbeatTimer()
        {
            if (_isHeartbeatRunning) return;
            _isHeartbeatRunning = true;

            // Heartbeat every 5 minutes (300,000 ms)
            _heartbeatTimer = new System.Threading.Timer(
                async _ => await SendHeartbeatAsync(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5)
            );
        }

        public void StopHeartbeatTimer()
        {
            _isHeartbeatRunning = false;
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        private static string GetServerUrl(string? overrideUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(overrideUrl))
                return overrideUrl.Trim();

            return ConfigService.CurrentSettings.LicenseServerUrl?.Trim() ?? string.Empty;
        }

        private static async Task<LicenseResponse> PostApiAsync(string url, LicenseRequest request)
        {
            try
            {
                string json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var httpResp = await _httpClient.PostAsync(url, content);
                string respJson = await httpResp.Content.ReadAsStringAsync();

                var responseObj = JsonSerializer.Deserialize<LicenseResponse>(respJson);
                return responseObj ?? new LicenseResponse { Success = false, Message = "Dữ liệu phản hồi từ máy chủ không hợp lệ." };
            }
            catch (Exception ex)
            {
                return new LicenseResponse
                {
                    Success = false,
                    Message = $"Lỗi kết nối tới License Server: {ex.Message}"
                };
            }
        }

        #region Local Token Storage (Encrypted with DPAPI)

        private static LocalLicenseToken? LoadLocalToken()
        {
            _tokenLock.Wait();
            try
            {
                if (!File.Exists(TokenFilePath)) return null;

                byte[] encryptedData = File.ReadAllBytes(TokenFilePath);
                byte[] decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decryptedData);

                return JsonSerializer.Deserialize<LocalLicenseToken>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LicenseService] LoadLocalToken failed: {ex.Message}");
                return null;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private static async Task SaveLocalTokenAsync(LocalLicenseToken token)
        {
            await _tokenLock.WaitAsync();
            try
            {
                string dir = Path.GetDirectoryName(TokenFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(token);
                byte[] rawData = Encoding.UTF8.GetBytes(json);
                byte[] encryptedData = ProtectedData.Protect(rawData, null, DataProtectionScope.CurrentUser);

                await File.WriteAllBytesAsync(TokenFilePath, encryptedData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LicenseService] SaveLocalToken failed: {ex.Message}");
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private static async Task DeleteLocalTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                if (File.Exists(TokenFilePath))
                {
                    File.Delete(TokenFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LicenseService] DeleteLocalToken failed: {ex.Message}");
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        #endregion

        public void Dispose()
        {
            StopHeartbeatTimer();
        }
    }
}
