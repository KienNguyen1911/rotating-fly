using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Application.Services
{
    public class GeminiCookieSyncService
    {
        private readonly Action<string> _log;
        private readonly IConfigService _configService;
        private readonly IBrowserService _browserService;

        public GeminiCookieSyncService(
            Action<string> log,
            IConfigService configService,
            IBrowserService browserService)
        {
            _log = log;
            _configService = configService;
            _browserService = browserService;
        }

        public string GetDefaultCookiesJsonPath()
        {
            // CRITICAL: cookies.json must live NEXT TO server.py — that's
            // what `Path(__file__).resolve().parent / "cookies.json"` reads
            // inside the Python server. Writing anywhere else (e.g. the
            // bin output folder) silently breaks authentication.
            //
            // We resolve by locating server.py first, so even when running
            // from a build output directory with no Modules/ next to it,
            // cookies still land where the Python process expects them.
            string serverScriptDir = FindServerScriptDirectory();
            if (!string.IsNullOrEmpty(serverScriptDir))
            {
                return Path.Combine(serverScriptDir, "cookies.json");
            }

            // Fallback for legacy code paths (should not be reached anymore).
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "cookies.json");
            if (!File.Exists(targetPath))
            {
                string devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Modules", "Gemini-API-2.0.0", "cookies.json"));
                if (Directory.Exists(Path.GetDirectoryName(devPath)))
                {
                    targetPath = devPath;
                }
            }
            return targetPath;
        }

        private static string FindServerScriptDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1) Same directory tree as the running exe (bin output copy).
            string candidate = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0");
            if (File.Exists(Path.Combine(candidate, "server.py"))) return candidate;

            // 2) Walk up to the project root (dev scenario: exe lives in
            //    bin/x64/Debug/net10.0-windows10.0.26100.0/ inside the
            //    AssetAutomator.WinUI project; the Modules folder is at the
            //    repo root).
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                candidate = Path.Combine(dir.FullName, "Modules", "Gemini-API-2.0.0");
                if (File.Exists(Path.Combine(candidate, "server.py"))) return candidate;

                candidate = Path.Combine(dir.FullName, "src", "Modules", "Gemini-API-2.0.0");
                if (File.Exists(Path.Combine(candidate, "server.py"))) return candidate;

                dir = dir.Parent;
            }

            // 3) Last-ditch: use the exact path PythonServerManager resolves.
            try
            {
                var mgrType = Type.GetType("AssetAutomator.Infrastructure.Helpers.PythonServerManager, AssetAutomator.Infrastructure");
                if (mgrType != null)
                {
                    var mgrInstance = mgrType.GetProperty("Default")?.GetValue(null);
                    var resolveMethod = mgrType.GetMethod("ResolveServerScriptPath");
                    if (resolveMethod != null)
                    {
                        string? scriptPath = resolveMethod.Invoke(mgrInstance, null) as string;
                        if (!string.IsNullOrEmpty(scriptPath))
                            return Path.GetDirectoryName(scriptPath)!;
                    }
                }
            }
            catch { }

            return string.Empty;
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
                await SaveCookiesJsonContentAsync(savePath, jsonContent);

                // Mirror to the alternate location (bin output folder) if it
                // differs from the Python script folder, so anyone running the
                // legacy fallback path also picks up the new cookies. This
                // keeps behaviour backward-compatible while the primary
                // path now writes next to server.py.
                MirrorToAlternateLocations(savePath);

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

        /// <summary>
        /// Import and format custom raw JSON or cookie key-value string directly into cookies.json
        /// </summary>
        public async Task<bool> SaveCustomCookiesJsonAsync(string rawInput, string? customSavePath = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawInput)) return false;

                string savePath = customSavePath ?? GetDefaultCookiesJsonPath();
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

                var cookieDict = new Dictionary<string, string>();

                rawInput = rawInput.Trim();
                if (rawInput.StartsWith("{"))
                {
                    using var doc = JsonDocument.Parse(rawInput);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("cookies", out var cookiesElem) && cookiesElem.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in cookiesElem.EnumerateObject())
                            {
                                cookieDict[prop.Name] = prop.Value.GetString() ?? "";
                            }
                        }
                        else
                        {
                            foreach (var prop in root.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    cookieDict[prop.Name] = prop.Value.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
                else if (rawInput.StartsWith("["))
                {
                    using var doc = JsonDocument.Parse(rawInput);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("name", out var nElem) && item.TryGetProperty("value", out var vElem))
                            {
                                string key = nElem.GetString() ?? "";
                                string val = vElem.GetString() ?? "";
                                if (!string.IsNullOrEmpty(key))
                                {
                                    cookieDict[key] = val;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Key=Value format or cookie header line
                    var pairs = rawInput.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pair in pairs)
                    {
                        var parts = pair.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string val = parts[1].Trim();
                            if (!string.IsNullOrEmpty(key))
                            {
                                cookieDict[key] = val;
                            }
                        }
                    }
                }

                if (cookieDict.Count == 0)
                {
                    _log("[COOKIE-SYNC] ⚠️ Dữ liệu cookie nhập vào không hợp lệ hoặc rỗng.");
                    return false;
                }

                var rootObj = new
                {
                    updated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"),
                    user_agent = DefaultUserAgent,
                    cookies = cookieDict
                };

                string jsonContent = JsonSerializer.Serialize(rootObj, new JsonSerializerOptions { WriteIndented = true });
                await SaveCookiesJsonContentAsync(savePath, jsonContent);

                ClearTempCookieCache();

                _log($"[COOKIE-SYNC] ✅ Đã nạp thành công {cookieDict.Count} Cookie Gemini vào file: {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ❌ Lỗi khi nhập Cookie: {ex.Message}");
                return false;
            }
        }

        private async Task SaveCookiesJsonContentAsync(string savePath, string jsonContent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            await File.WriteAllTextAsync(savePath, jsonContent);

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string rootModulesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Modules", "Gemini-API-2.0.0"));
                if (Directory.Exists(rootModulesDir))
                {
                    string rootSavePath = Path.Combine(rootModulesDir, "cookies.json");
                    if (!string.Equals(Path.GetFullPath(savePath), Path.GetFullPath(rootSavePath), StringComparison.OrdinalIgnoreCase))
                    {
                        await File.WriteAllTextAsync(rootSavePath, jsonContent);
                        _log($"[COOKIE-SYNC] 📁 Đã đồng bộ thêm vào file nguồn gốc: {rootSavePath}");
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Mirrors a freshly-written cookies.json to the legacy bin output
        /// folder (if it differs from the canonical location) so any code path
        /// that still tries to read cookies.json from AppDomain.BaseDirectory
        /// also gets the latest cookies. No-op when both paths collapse to
        /// the same file.
        /// </summary>
        private async Task MirrorToAlternateLocations(string canonicalPath)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string altPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "cookies.json");
                if (string.Equals(Path.GetFullPath(canonicalPath), Path.GetFullPath(altPath), StringComparison.OrdinalIgnoreCase))
                    return;

                if (File.Exists(altPath) || Directory.Exists(Path.GetDirectoryName(altPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(altPath)!);
                    string content = await File.ReadAllTextAsync(canonicalPath);
                    await File.WriteAllTextAsync(altPath, content);
                    _log($"[COOKIE-SYNC] 🪞 Đã mirror cookies sang bin folder: {altPath}");
                }
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ⚠️ Mirror sang bin folder thất bại: {ex.Message}");
            }
        }

        /// <summary>
        /// Mirror the freshly-written cookies.json to every other well-known
        /// location on disk so the Python server cannot accidentally read
        /// a stale copy. Idempotent — skips paths identical to the source.
        /// </summary>
        private void MirrorCookiesToAlternateLocations(string primarySavePath)
        {
            try
            {
                string content = File.ReadAllText(primarySavePath);
                string primaryFull = Path.GetFullPath(primarySavePath);

                // Candidate directories that historically held cookies.json
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new List<string>
                {
                    // Bin output copy (where most legacy code used to write)
                    Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "cookies.json"),
                    // Project source root (next to server.py when running
                    // Python directly from the repo)
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Modules", "Gemini-API-2.0.0", "cookies.json")),
                };

                foreach (string alt in candidates)
                {
                    try
                    {
                        string altFull = Path.GetFullPath(alt);
                        if (string.Equals(altFull, primaryFull, StringComparison.OrdinalIgnoreCase)) continue;

                        // Only overwrite if the directory exists or we can create it
                        string dir = Path.GetDirectoryName(alt)!;
                        if (!Directory.Exists(dir)) continue;

                        File.WriteAllText(alt, content);
                        _log($"[COOKIE-SYNC] 🔁 Đã mirror cookies.json sang: {alt}");
                    }
                    catch (Exception ex)
                    {
                        _log($"[COOKIE-SYNC] ⚠️ Không thể mirror tới {alt}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ⚠️ MirrorCookiesToAlternateLocations thất bại: {ex.Message}");
            }
        }

        /// <summary>
        /// Automatically scans system Chrome and app Chrome profile directories, creates a minimal profile clone,
        /// and uses Playwright to extract Gemini session cookies.
        /// Optimized: only copies essential cookie files (not entire profile), skips unnecessary page navigation.
        /// Tries headless first, then falls back to headed mode for app profiles if headless fails.
        /// </summary>
        public async Task<bool> AutoSyncFromSystemChromeAsync()
        {
            _log("[COOKIE-SYNC] 🔍 Đang đọc Cookies từ trình duyệt Chrome hệ thống...");

            var candidateDirs = new List<string>();

            // App's ChromeProfiles directory
            string appProfilesDir = _configService.CurrentSettings.ChromeProfilesDir;
            if (Directory.Exists(appProfilesDir))
            {
                foreach (var sub in Directory.GetDirectories(appProfilesDir))
                {
                    candidateDirs.Add(sub);
                }
            }

            // System Chrome User Data directory
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string systemChromeUserData = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (Directory.Exists(systemChromeUserData))
            {
                string defaultProf = Path.Combine(systemChromeUserData, "Default");
                if (Directory.Exists(defaultProf)) candidateDirs.Add(defaultProf);

                try
                {
                    foreach (var sub in Directory.GetDirectories(systemChromeUserData, "Profile *"))
                    {
                        candidateDirs.Add(sub);
                    }
                }
                catch { }
            }

            if (candidateDirs.Count == 0)
            {
                _log("[COOKIE-SYNC] ⚠️ Không tìm thấy thư mục Profile Chrome nào trên hệ thống.");
                return false;
            }

            // ── Phase 1: Thử headless mode (nhanh, không popup) ──
            bool success = await TryExtractCookiesFromProfilesAsync(candidateDirs, headless: true);
            if (success) return true;

            // ── Phase 2: Fallback headed mode cho app profiles (có cửa sổ hiển thị) ──
            _log("[COOKIE-SYNC] 🔄 Headless không tìm thấy cookie. Thử lại với chế độ hiển thị (headed)...");
            var appProfileDirs = candidateDirs
                .Where(d => d.StartsWith(appProfilesDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (appProfileDirs.Count > 0)
            {
                success = await TryExtractCookiesFromProfilesAsync(appProfileDirs, headless: false);
                if (success) return true;
            }

            _log("[COOKIE-SYNC] ⚠️ Chưa tìm thấy phiên đăng nhập Gemini sẵn có trong các profile Chrome.");
            return false;
        }

        private async Task<bool> TryExtractCookiesFromProfilesAsync(List<string> candidateDirs, bool headless)
        {
            using var playwright = await Playwright.CreateAsync();

            foreach (var sourceProfilePath in candidateDirs)
            {
                _log($"[COOKIE-SYNC] 🚀 Đang đọc Cookies từ Profile: {Path.GetFileName(sourceProfilePath)} (headless={headless})...");

                string tempProfilePath = Path.Combine(Path.GetTempPath(), "GeminiCookieSync_" + Guid.NewGuid().ToString("N"));
                try
                {
                    // OPTIMIZED: Only copy essential cookie-related files (~1-5MB), not entire profile (~200-500MB+)
                    _browserService.CopyMinimalProfileForCookies(sourceProfilePath, tempProfilePath);

                    var launchArgs = new List<string>
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox",
                        "--disable-extensions",
                        "--disable-gpu",
                        "--disable-background-networking",
                        "--disable-sync"
                    };

                    await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
                        tempProfilePath,
                        new BrowserTypeLaunchPersistentContextOptions
                        {
                            Headless = headless,
                            Channel = "chrome",
                            Args = launchArgs
                        });

                    // OPTIMIZED: Extract cookies directly from the context without navigating to any URL.
                    // Chrome loads cookies from the profile DB on launch — no page load needed.
                    bool success = await SyncCookiesFromBrowserContextAsync(context);
                    await context.CloseAsync();

                    if (success)
                    {
                        _log($"[COOKIE-SYNC] 🎉 Trích xuất Cookie Gemini thành công từ Chrome profile ({Path.GetFileName(sourceProfilePath)})!");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _log($"[COOKIE-SYNC] ⚠️ Bỏ qua profile {Path.GetFileName(sourceProfilePath)}: {ex.Message}");
                }
                finally
                {
                    _browserService.CleanupTempProfile(tempProfilePath);
                }
            }

            _log("[COOKIE-SYNC] ⚠️ Chưa tìm thấy phiên đăng nhập Gemini sẵn có trong các profile Chrome.");
            return false;
        }
    }
}