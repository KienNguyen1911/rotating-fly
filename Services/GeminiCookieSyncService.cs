using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AssetAutomator
{
    public class GeminiCookieSyncService
    {
        private readonly Action<string> _log;

        public GeminiCookieSyncService(Action<string> log)
        {
            _log = log;
        }

        public string GetDefaultCookiesJsonPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "cookies.json");

            if (!File.Exists(targetPath))
            {
                // Fallback to project root if running under dev build
                string devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Modules", "Gemini-API-2.0.0", "cookies.json"));
                if (Directory.Exists(Path.GetDirectoryName(devPath)))
                {
                    targetPath = devPath;
                }
            }

            return targetPath;
        }

        private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

        private static readonly HashSet<string> PreferredCookieNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "__Secure-1PSID",
            "__Secure-1PSIDTS",
            "__Secure-1PAPISID",
            "__Secure-3PSID",
            "__Secure-3PSIDTS",
            "__Secure-3PAPISID",
            "SID",
            "HSID",
            "SSID",
            "APISID",
            "SAPISID",
            "NID",
            "AEC",
            "CONSENT",
            "1P_JAR"
        };

        /// <summary>
        /// Clears stale .cached_cookies_*.json files in %TEMP%\gemini_webapi to prevent Python from loading old cached tokens.
        /// </summary>
        public void ClearTempCookieCache()
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string cacheDir = Path.Combine(tempDir, "gemini_webapi");
                if (Directory.Exists(cacheDir))
                {
                    var files = Directory.GetFiles(cacheDir, "*.json");
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                    _log("[COOKIE-SYNC] 🧹 Đã dọn dẹp cache cookie tạm thời trong %TEMP%\\gemini_webapi.");
                }
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ⚠️ Không thể dọn cache cookie tạm: {ex.Message}");
            }
        }

        /// <summary>
        /// Syncs cookies directly from an active Playwright IBrowserContext to cookies.json
        /// </summary>
        public async Task<bool> SyncCookiesFromBrowserContextAsync(IBrowserContext context, string? customSavePath = null)
        {
            try
            {
                var cookies = await context.CookiesAsync(new[] { "https://gemini.google.com", "https://google.com" });
                var psidCookie = cookies.FirstOrDefault(c => c.Name == "__Secure-1PSID");

                if (psidCookie == null || string.IsNullOrWhiteSpace(psidCookie.Value))
                {
                    _log("[COOKIE-SYNC] ⚠️ Chưa tìm thấy __Secure-1PSID trong phiên Chrome. Hãy đảm bảo bạn đã đăng nhập https://gemini.google.com trên trình duyệt.");
                    return false;
                }

                // Try extracting navigator.userAgent from open page
                string userAgent = DefaultUserAgent;
                try
                {
                    var pages = context.Pages;
                    if (pages.Count > 0)
                    {
                        string pageUa = await pages[0].EvaluateAsync<string>("navigator.userAgent");
                        if (!string.IsNullOrWhiteSpace(pageUa))
                        {
                            userAgent = pageUa;
                        }
                    }
                }
                catch { }

                string savePath = customSavePath ?? GetDefaultCookiesJsonPath();
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

                var cookieDict = new Dictionary<string, string>();
                // First pass: add preferred/essential cookies
                foreach (var c in cookies)
                {
                    if (PreferredCookieNames.Contains(c.Name))
                    {
                        cookieDict[c.Name] = c.Value;
                    }
                }
                // Second pass: add remaining cookies if not already present
                foreach (var c in cookies)
                {
                    if (!cookieDict.ContainsKey(c.Name))
                    {
                        cookieDict[c.Name] = c.Value;
                    }
                }

                var rootObj = new
                {
                    updated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"),
                    user_agent = userAgent,
                    cookies = cookieDict
                };

                string jsonContent = JsonSerializer.Serialize(rootObj, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(savePath, jsonContent);

                // Invalidate Python temp cookie cache
                ClearTempCookieCache();

                _log($"[COOKIE-SYNC] ✅ Đã tự động cập nhật Cookie Gemini thành công vào file: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ❌ Lỗi khi đồng bộ Cookie từ trình duyệt: {ex.Message}");
                return false;
            }
        }
    }
}
