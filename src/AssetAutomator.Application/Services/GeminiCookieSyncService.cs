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
    /// <summary>
    /// Two-phase cookie sync for Gemini:
    ///   1) Import raw cookie text → save cookies.json + open Playwright on the
    ///      configured persistent profile at <see cref="IConfigService.CurrentSettings.ChromeProfilesDir"/>/GeminiProfile,
    ///      inject cookies, navigate to verify session is valid.
    ///   2) Refresh — open the SAME persistent profile, extract cookies via
    ///      Playwright, write cookies.json. No user input required.
    /// </summary>
    public class GeminiCookieSyncService
    {
        private const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

        private const string ProfileFolderName = "GeminiProfile";

        private static readonly HashSet<string> PreferredCookieNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "__Secure-1PSID",
            "__Secure-1PSIDTS",
            "__Secure-1PSIDCC",
            "__Secure-1PSIDRTS",
            "__Secure-1PAPISID",
            "__Secure-3PSID",
            "__Secure-3PSIDTS",
            "__Secure-3PSIDCC",
            "__Secure-3PSIDRTS",
            "__Secure-3PAPISID",
            "SID",
            "HSID",
            "SSID",
            "APISID",
            "SAPISID",
            "NID",
            "AEC",
            "CONSENT",
            "1P_JAR",
            "SIDCC",
        };

        private readonly Action<string> _log;
        private readonly IConfigService _configService;

        public GeminiCookieSyncService(Action<string> log, IConfigService configService)
        {
            _log = log;
            _configService = configService;
        }

        // ─────────────────────────────────────────────────────
        //  Path resolvers
        // ─────────────────────────────────────────────────────

        public string GetDefaultCookiesJsonPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "cookies.json");

            if (!File.Exists(targetPath))
            {
                string devPath = Path.GetFullPath(Path.Combine(
                    baseDir, "..", "..", "..", "Modules", "Gemini-API-2.0.0", "cookies.json"));
                if (Directory.Exists(Path.GetDirectoryName(devPath)))
                {
                    targetPath = devPath;
                }
            }

            return targetPath;
        }

        public string GetResolvedProfilePath()
        {
            string profilesDir = _configService.CurrentSettings.ChromeProfilesDir;
            if (string.IsNullOrWhiteSpace(profilesDir))
            {
                profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
            }

            return Path.Combine(profilesDir, ProfileFolderName);
        }

        public bool HasSavedProfile()
        {
            string path = GetResolvedProfilePath();
            if (!Directory.Exists(path)) return false;
            // Treat profile as "saved" only if it has a real Chrome profile marker
            return File.Exists(Path.Combine(path, "Local State"))
                || File.Exists(Path.Combine(path, "Default", "Local State"))
                || File.Exists(Path.Combine(path, "Preferences"))
                || File.Exists(Path.Combine(path, "Default", "Preferences"));
        }

        // ─────────────────────────────────────────────────────
        //  Phase 1 — paste cookies → save JSON + persistent profile
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Imports raw cookie text (JSON object / JSON array / key=value string / whole cookies.json),
        /// writes cookies.json so the Python server can pick it up immediately, then opens the
        /// Playwright persistent profile at the configured path, injects cookies, and navigates
        /// to gemini.google.com.
        ///
        /// The browser stays OPEN so the user can complete any manual step Google demands
        /// (2FA, CAPTCHA, suspicious-login check, etc.). A WPF dialog confirms when to
        /// re-extract cookies from the running browser and close it.
        /// </summary>
        /// <param name="rawInput">Cookie text from the user (popup).</param>
        /// <param name="awaitUserConfirmation">
        /// If true, blocks on a WPF dialog asking the user to confirm once they see the
        /// Gemini home page. If false, only navigates + extracts + closes (used by tests).
        /// </param>
        /// <param name="parentWindow">Owner for the confirmation dialog (WPF window).</param>
        public async Task<(bool Success, string Message, string ProfilePath)> ImportCookiesFromTextAsync(
            string rawInput,
            bool awaitUserConfirmation = true,
            System.Windows.Window? parentWindow = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawInput))
                    return (false, "Vui lòng dán cookies vào ô nhập.", string.Empty);

                var cookies = ParseRawCookieInput(rawInput);
                if (cookies.Count == 0)
                {
                    return (false, "Không đọc được cookie hợp lệ từ dữ liệu đã nhập.", string.Empty);
                }

                if (!cookies.ContainsKey("__Secure-1PSID") && !cookies.ContainsKey("__Secure-3PSID"))
                {
                    _log("[COOKIE-SYNC] ⚠️ Cảnh báo: cookies thiếu __Secure-1PSID/__Secure-3PSID — phiên có thể đã hết hạn.");
                }

                // We keep updating cookies.json AFTER the user confirms in the browser — the
                // first write happens below as a snapshot, then a final authoritative write
                // replaces it once the session is verified on gemini.google.com.
                string savePath = GetDefaultCookiesJsonPath();
                string profilePath = GetResolvedProfilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);

                bool profileExists = HasSavedProfile();
                if (profileExists)
                {
                    _log($"[COOKIE-SYNC] 📁 Phát hiện Chrome profile đã có tại: {profilePath}");
                    _log("[COOKIE-SYNC] ♻️ Sẽ mở profile này và inject cookies mới (giữ các session site khác).");
                }
                else
                {
                    _log($"[COOKIE-SYNC] 🆕 Tạo Chrome profile mới tại: {profilePath}");
                }

                // Write a snapshot so the Python server can attempt with the pasted cookies
                // even before the user confirms the manual login.
                await WriteCookiesJsonAsync(cookies, savePath, DefaultUserAgent);
                _log($"[COOKIE-SYNC] 💾 Đã lưu snapshot {cookies.Count} cookies vào {savePath}");

                var (verifyOk, verifyMsg, finalCookies, finalUa) = await OpenProfileAndInjectThenWaitForUserAsync(
                    profilePath, cookies, awaitUserConfirmation, parentWindow);

                if (!verifyOk)
                {
                    return (false, verifyMsg, profilePath);
                }

                // Final authoritative write — uses the actual cookies that survived in the browser
                // (Google may have merged, refreshed or added new ones during the manual login).
                await WriteCookiesJsonAsync(finalCookies, savePath, finalUa);
                _log($"[COOKIE-SYNC] 💾 Đã lưu cuối cùng {finalCookies.Count} cookies vào {savePath}");

                ClearTempCookieCache();
                _log($"[COOKIE-SYNC] ✅ Import cookies thành công. Profile: {profilePath}");

                return (true, $"Đã nạp {finalCookies.Count} cookies và lưu Chrome profile tại {profilePath}", profilePath);
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ❌ Lỗi ImportCookiesFromTextAsync: {ex.Message}");
                return (false, $"Lỗi: {ex.Message}", string.Empty);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Phase 2 — refresh cookies from saved profile
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Opens the saved Chrome profile at the configured path, navigates to gemini.google.com
        /// to force the cookie SQLite to reload, extracts cookies via Playwright, and writes
        /// cookies.json. Returns false with a hint to re-import if the session appears expired.
        /// </summary>
        public async Task<(bool Success, string Message, bool SessionExpired)> RefreshCookiesFromSavedProfileAsync()
        {
            string profilePath = GetResolvedProfilePath();
            if (!HasSavedProfile())
            {
                return (false, "Chưa có Chrome profile được lưu. Vui lòng bấm 'Nhập Cookies' để tạo profile trước.", false);
            }

            try
            {
                ClearTempCookieCache();
                _log($"[COOKIE-SYNC] 🔄 Đang mở profile đã lưu: {profilePath}");

                var (extractedOk, extractedCookies, userAgent, finalUrl, sessionExpired) =
                    await OpenProfileAndExtractAsync(profilePath);

                if (sessionExpired)
                {
                    return (false, "Phiên đăng nhập Gemini trong profile đã hết hạn. Vui lòng bấm 'Nhập Cookies' để dán cookies mới.", true);
                }

                if (!extractedOk || extractedCookies.Count == 0)
                {
                    return (false, $"Không trích xuất được cookies từ profile: {finalUrl}", false);
                }

                string savePath = GetDefaultCookiesJsonPath();
                await WriteCookiesJsonAsync(extractedCookies, savePath, userAgent);
                _log($"[COOKIE-SYNC] 💾 Đã cập nhật {extractedCookies.Count} cookies vào {savePath}");

                ClearTempCookieCache();
                return (true, $"Refresh thành công {extractedCookies.Count} cookies từ profile.", false);
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ❌ Lỗi RefreshCookiesFromSavedProfileAsync: {ex.Message}");
                return (false, $"Lỗi refresh: {ex.Message}", false);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Playwright — open profile, inject cookies, wait for user to confirm
        // ─────────────────────────────────────────────────────

        private async Task<(bool Success, string Message, Dictionary<string, string> Cookies, string UserAgent)>
            OpenProfileAndInjectThenWaitForUserAsync(
            string profilePath,
            Dictionary<string, string> cookiesDict,
            bool awaitUserConfirmation,
            System.Windows.Window? parentWindow)
        {
            using var playwright = await Playwright.CreateAsync();
            await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
                profilePath,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Channel = "chrome",
                    IgnoreDefaultArgs = new[] { "--enable-automation" },
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox",
                        "--disable-infobars",
                    }
                });

            try
            {
                var cookies = ConvertToPlaywrightCookies(cookiesDict);
                await context.AddCookiesAsync(cookies);
                _log($"[COOKIE-SYNC] 🍪 Đã inject {cookies.Count} cookies vào Playwright context.");

                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync("https://gemini.google.com",
                        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
                }
                catch (Exception navEx)
                {
                    _log($"[COOKIE-SYNC] ⚠️ Navigate warning: {navEx.Message}");
                }

                // Auto-detect: nếu URL là gemini.google.com (không phải accounts.google.com / signin)
                // → user KHÔNG CẦN làm gì, chỉ cần bấm xác nhận.
                // Nếu Google redirect về login → user CẦN đăng nhập thủ công trong browser đang mở.
                await Task.Delay(2500);
                string urlAfterAutoNav = page.Url;
                bool autoLoginOk = !IsLoginRedirectUrl(urlAfterAutoNav);
                _log($"[COOKIE-SYNC] 🌐 URL sau auto-navigate: {urlAfterAutoNav}");
                if (autoLoginOk)
                {
                    _log("[COOKIE-SYNC] ✅ Cookies được Google chấp nhận — user chỉ cần xác nhận.");
                }
                else
                {
                    _log("[COOKIE-SYNC] ⚠️ Google redirect về trang đăng nhập — user cần login thủ công trong browser.");
                }

                if (awaitUserConfirmation)
                {
                    string msg = autoLoginOk
                        ? "Chrome đã mở và Google CHẤP NHẬN cookies của bạn.\n\n" +
                          "Gemini đang hiển thị trang chủ — bạn không cần làm gì thêm.\n\n" +
                          "Bấm 'Xác nhận' để trích xuất cookies từ browser và lưu vào cookies.json.\n" +
                          "Bấm 'Hủy' để đóng browser và hủy thao tác."
                        : "Chrome đã mở nhưng Google YÊU CẦU đăng nhập thủ công.\n\n" +
                          "Vui lòng đăng nhập tài khoản Google của bạn trong cửa sổ Chrome vừa hiện ra " +
                          "(xử lý 2FA / CAPTCHA / suspicious-login nếu có).\n\n" +
                          "Sau khi đăng nhập xong và thấy giao diện Gemini, bấm 'Xác nhận' để trích xuất cookies.\n" +
                          "Bấm 'Hủy' để đóng browser và hủy thao tác.";

                    var confirmResult = System.Windows.MessageBox.Show(
                        parentWindow
                            ?? (System.Windows.Application.Current?.MainWindow as System.Windows.Window)
                            ?? new System.Windows.Window(),
                        msg,
                        "🔑 Nhập Cookies Gemini — Xác nhận",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Information);

                    if (confirmResult != System.Windows.MessageBoxResult.Yes)
                    {
                        _log("[COOKIE-SYNC] ⏹️ User hủy — đóng browser.");
                        return (false, "User đã hủy thao tác nhập cookies.", new Dictionary<string, string>(), DefaultUserAgent);
                    }

                    // Re-check URL after user confirmation — they may have logged in successfully
                    // OR they may still be on the login screen (in which case we still extract
                    // whatever cookies are present, but treat as failure).
                    await Task.Delay(1500);
                    string finalUrl = page.Url;
                    _log($"[COOKIE-SYNC] 🌐 URL sau xác nhận: {finalUrl}");

                    if (IsLoginRedirectUrl(finalUrl))
                    {
                        return (false,
                            "Sau khi xác nhận vẫn đang ở trang đăng nhập. Vui lòng thử lại với cookies mới hơn.",
                            new Dictionary<string, string>(), DefaultUserAgent);
                    }
                }

                // Extract whatever cookies survive in the browser right now (Google may have
                // merged with the pasted cookies and added AEC / 1P_JAR / NID, etc.).
                var liveCookies = await context.CookiesAsync(new[] { "https://gemini.google.com", "https://google.com" });
                var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in liveCookies)
                {
                    if (!string.IsNullOrEmpty(c.Value))
                    {
                        extracted[c.Name] = c.Value;
                    }
                }

                string userAgent = DefaultUserAgent;
                try
                {
                    string pageUa = await page.EvaluateAsync<string>("navigator.userAgent");
                    if (!string.IsNullOrWhiteSpace(pageUa)) userAgent = pageUa;
                }
                catch { }

                return (true, "Session verified on gemini.google.com.", extracted, userAgent);
            }
            finally
            {
                try
                {
                    await context.CloseAsync();
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────────────
        //  Playwright — open profile, extract cookies
        // ─────────────────────────────────────────────────────

        private async Task<(bool Success, Dictionary<string, string> Cookies, string UserAgent, string FinalUrl, bool SessionExpired)>
            OpenProfileAndExtractAsync(string profilePath)
        {
            using var playwright = await Playwright.CreateAsync();
            await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
                profilePath,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = true,
                    Channel = "chrome",
                    IgnoreDefaultArgs = new[] { "--enable-automation" },
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox",
                        "--disable-extensions",
                        "--disable-gpu",
                        "--disable-background-networking",
                        "--disable-sync",
                    }
                });

            try
            {
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync("https://gemini.google.com",
                        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                }
                catch (Exception navEx)
                {
                    _log($"[COOKIE-SYNC] ⚠️ Navigate warning: {navEx.Message}");
                }

                string finalUrl = page.Url;
                _log($"[COOKIE-SYNC] 🌐 URL sau navigate: {finalUrl}");

                if (IsLoginRedirectUrl(finalUrl))
                {
                    return (false, new Dictionary<string, string>(), DefaultUserAgent, finalUrl, true);
                }

                var cookies = await context.CookiesAsync(new[] { "https://gemini.google.com", "https://google.com" });
                var psid = cookies.FirstOrDefault(c => c.Name == "__Secure-1PSID");
                if (psid == null || string.IsNullOrWhiteSpace(psid.Value))
                {
                    return (false, new Dictionary<string, string>(), DefaultUserAgent, finalUrl, true);
                }

                var ordered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in PreferredCookieNames)
                {
                    var matched = cookies.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (matched != null && !string.IsNullOrEmpty(matched.Value))
                    {
                        ordered[matched.Name] = matched.Value;
                    }
                }
                foreach (var c in cookies)
                {
                    if (!ordered.ContainsKey(c.Name) && !string.IsNullOrEmpty(c.Value))
                    {
                        ordered[c.Name] = c.Value;
                    }
                }

                string userAgent = DefaultUserAgent;
                try
                {
                    string pageUa = await page.EvaluateAsync<string>("navigator.userAgent");
                    if (!string.IsNullOrWhiteSpace(pageUa)) userAgent = pageUa;
                }
                catch { }

                return (true, ordered, userAgent, finalUrl, false);
            }
            finally
            {
                try { await context.CloseAsync(); } catch { }
            }
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static bool IsLoginRedirectUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase)
                || url.Contains("signin", StringComparison.OrdinalIgnoreCase);
        }

        private async Task WriteCookiesJsonAsync(
            Dictionary<string, string> cookiesDict,
            string savePath,
            string userAgent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

            var rootObj = new
            {
                updated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"),
                user_agent = userAgent,
                cookies = cookiesDict
            };

            string json = JsonSerializer.Serialize(rootObj, new JsonSerializerOptions { WriteIndented = true });
            string tmp = savePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            // Atomic move to avoid half-written file if process dies mid-write
            File.Move(tmp, savePath, overwrite: true);
        }

        /// <summary>
        /// Injects a <c>__Secure-1PSID</c>-style cookie dict into the Playwright context.
        /// Playwright needs explicit domain/path per cookie; we map every cookie to
        /// <c>.google.com</c> with secure/sameSite flags appropriate for Google's auth cookies.
        /// </summary>
        private static List<Microsoft.Playwright.Cookie> ConvertToPlaywrightCookies(Dictionary<string, string> cookies)
        {
            const string domain = ".google.com";
            var list = new List<Microsoft.Playwright.Cookie>();
            foreach (var kv in cookies)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                bool isSecure = kv.Key.StartsWith("__Secure-", StringComparison.OrdinalIgnoreCase)
                              || kv.Key.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase);
                list.Add(new Microsoft.Playwright.Cookie
                {
                    Name = kv.Key,
                    Value = kv.Value ?? string.Empty,
                    Domain = domain,
                    Path = "/",
                    Secure = isSecure,
                    HttpOnly = false,
                    SameSite = isSecure
                        ? Microsoft.Playwright.SameSiteAttribute.None
                        : Microsoft.Playwright.SameSiteAttribute.Lax,
                    Expires = -1,
                });
            }
            return list;
        }

        /// <summary>
        /// Parses raw cookie input in any of the supported formats:
        ///  - JSON object:   {"name": "value", ...}
        ///  - JSON array:    [{"name": "x", "value": "y", ...}, ...]
        ///  - Whole cookies.json: {"updated_at": "...", "cookies": {"name": "value"}}
        ///  - key=value; key=value string
        /// </summary>
        private static Dictionary<string, string> ParseRawCookieInput(string rawInput)
        {
            var cookieDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                            if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                cookieDict[prop.Name] = prop.Value.GetString() ?? "";
                            }
                            else
                            {
                                cookieDict[prop.Name] = prop.Value.ToString();
                            }
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
                            string val = vElem.ValueKind == JsonValueKind.String
                                ? (vElem.GetString() ?? "")
                                : vElem.ToString();
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

            return cookieDict;
        }

        /// <summary>
        /// Clears stale cached cookie files in %TEMP%\gemini_webapi so the Python server
        /// cannot pick up a previously-cached session after we update cookies.json.
        /// </summary>
        public void ClearTempCookieCache()
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string cacheDir = Path.Combine(tempDir, "gemini_webapi");
                if (Directory.Exists(cacheDir))
                {
                    foreach (var file in Directory.GetFiles(cacheDir, "*.json"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    _log("[COOKIE-SYNC] 🧹 Đã dọn dẹp cache cookie tạm thời trong %TEMP%\\gemini_webapi.");
                }
            }
            catch (Exception ex)
            {
                _log($"[COOKIE-SYNC] ⚠️ Không thể dọn cache cookie tạm: {ex.Message}");
            }
        }
    }
}
