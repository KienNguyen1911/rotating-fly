using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AutoCreateImage
{
    public class ChatGptAccount
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("quota")]
        public int Quota { get; set; }

        [JsonPropertyName("refreshed_at")]
        public string RefreshedAt { get; set; } = string.Empty;
    }

    public class OAuthStartResult
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("authorize_url")]
        public string AuthorizeUrl { get; set; } = string.Empty;

        [JsonPropertyName("redirect_uri_prefix")]
        public string RedirectUriPrefix { get; set; } = string.Empty;
    }

    public class ApiServerManager
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private Process? _apiProcess;
        private readonly string _authKey = "chatgpt2api";
        private readonly string _baseUrl = "http://localhost:8000";

        public event Action<string>? LogReceived;
        public event Action? StatusChanged;

        public bool IsRunning => _apiProcess != null && !_apiProcess.HasExited;

        private void KillPortOwner(int port)
        {
            try
            {
                ProcessStartInfo killStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port}') do taskkill /F /PID %a\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(killStartInfo);
                proc?.WaitForExit();
                LogReceived?.Invoke($"[INFO] Đã giải phóng cổng {port} thành công.");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[WARNING] Không thể giải phóng cổng {port}: {ex.Message}");
            }
        }

        public void StartServer()
        {
            if (IsRunning) return;

            // Giải phóng cổng 8000 để tránh xung đột với các tiến trình chạy ngầm cũ (kể cả python dev)
            KillPortOwner(8000);

            // Kill any leftover Chatgpt2Server processes to free up port 8000
            try
            {
                foreach (var proc in Process.GetProcessesByName("Chatgpt2Server"))
                {
                    LogReceived?.Invoke($"[INFO] Đang tắt tiến trình Chatgpt2Server cũ (PID: {proc.Id}) để giải phóng cổng...");
                    proc.Kill(true);
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[WARNING] Không thể tắt tiến trình Chatgpt2Server cũ: {ex.Message}");
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string prodExe = Path.Combine(appDir, "Chatgpt2Server", "Chatgpt2Server.exe");

            ProcessStartInfo startInfo;
            if (File.Exists(prodExe))
            {
                LogReceived?.Invoke($"[INFO] Phát hiện bản build production. Đang khởi động API Server từ: {prodExe}");
                startInfo = new ProcessStartInfo
                {
                    FileName = prodExe,
                    WorkingDirectory = Path.Combine(appDir, "Chatgpt2Server"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }
            else
            {
                // Development path or base execution path
                string chatgpt2apiDir = Path.GetFullPath(Path.Combine(appDir, "Chatgpt2Api"));
                if (!Directory.Exists(chatgpt2apiDir))
                {
                    // Try relative to project root in dev mode
                    chatgpt2apiDir = Path.GetFullPath(Path.Combine(appDir, "..\\..\\..\\Chatgpt2Api"));
                }

                if (!Directory.Exists(chatgpt2apiDir))
                {
                    LogReceived?.Invoke($"[ERROR] Không tìm thấy thư mục Chatgpt2Api tại: {chatgpt2apiDir}");
                    return;
                }

                string pythonExe = Path.Combine(chatgpt2apiDir, ".venv", "Scripts", "python.exe");
                if (!File.Exists(pythonExe))
                {
                    // Fallback to system python
                    pythonExe = "python";
                    LogReceived?.Invoke("[WARNING] Không tìm thấy .venv/Scripts/python.exe, sử dụng 'python' hệ thống.");
                }

                string mainScript = Path.Combine(chatgpt2apiDir, "main.py");
                if (!File.Exists(mainScript))
                {
                    LogReceived?.Invoke($"[ERROR] Không tìm thấy file script chính tại: {mainScript}");
                    return;
                }

                LogReceived?.Invoke($"[INFO] Đang khởi động API Server từ: {chatgpt2apiDir}");
                startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{mainScript}\"",
                    WorkingDirectory = chatgpt2apiDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }

            _apiProcess = new Process { StartInfo = startInfo };
            _apiProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) LogReceived?.Invoke(e.Data);
            };
            _apiProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) LogReceived?.Invoke($"[Err] {e.Data}");
            };

            try
            {
                _apiProcess.Start();
                _apiProcess.BeginOutputReadLine();
                _apiProcess.BeginErrorReadLine();
                StatusChanged?.Invoke();
                LogReceived?.Invoke("[INFO] API Server đã khởi chạy thành công.");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[ERROR] Không thể khởi động tiến trình Python: {ex.Message}");
                _apiProcess = null;
                StatusChanged?.Invoke();
            }
        }

        public void StopServer()
        {
            if (_apiProcess != null && !_apiProcess.HasExited)
            {
                LogReceived?.Invoke("[INFO] Đang tắt API Server...");
                try
                {
                    _apiProcess.Kill(true); // Kill process tree (python + uvicorn)
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"[ERROR] Lỗi khi tắt tiến trình: {ex.Message}");
                }
                finally
                {
                    _apiProcess.Dispose();
                    _apiProcess = null;
                    StatusChanged?.Invoke();
                    LogReceived?.Invoke("[INFO] API Server đã dừng.");
                }
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? jsonContent = null)
        {
            var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authKey);
            if (jsonContent != null)
            {
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            }
            return request;
        }

        public async Task<List<ChatGptAccount>> GetAccountsAsync()
        {
            try
            {
                using var request = CreateRequest(HttpMethod.Get, "/api/accounts");
                using var response = await HttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("items", out var itemsProp))
                    {
                        var accounts = JsonSerializer.Deserialize<List<ChatGptAccount>>(itemsProp.GetRawText());
                        return accounts ?? new List<ChatGptAccount>();
                    }
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[API Error] Lỗi lấy danh sách tài khoản: {ex.Message}");
            }
            return new List<ChatGptAccount>();
        }

        public async Task<OAuthStartResult?> StartOAuthLoginAsync(string emailHint)
        {
            try
            {
                string jsonBody = JsonSerializer.Serialize(new { email_hint = emailHint });
                using var request = CreateRequest(HttpMethod.Post, "/api/accounts/oauth/start", jsonBody);
                using var response = await HttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<OAuthStartResult>(json);
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    LogReceived?.Invoke($"[API Error] Start OAuth thất bại: {err}");
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[API Error] Lỗi gọi start oauth: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> FinishOAuthLoginAsync(string sessionId, string callbackUrl)
        {
            try
            {
                string jsonBody = JsonSerializer.Serialize(new { session_id = sessionId, callback = callbackUrl });
                using var request = CreateRequest(HttpMethod.Post, "/api/accounts/oauth/finish", jsonBody);
                using var response = await HttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string res = await response.Content.ReadAsStringAsync();
                    LogReceived?.Invoke($"[API Success] Finish OAuth thành công: {res}");
                    return true;
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    LogReceived?.Invoke($"[API Error] Finish OAuth thất bại: {err}");
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[API Error] Lỗi gọi finish oauth: {ex.Message}");
            }
            return false;
        }
    }
}
