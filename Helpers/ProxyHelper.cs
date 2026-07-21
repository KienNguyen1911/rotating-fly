using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AssetAutomator
{
    /// <summary>
    /// Helper class for loading proxies from JSON file and validating if they are currently functional.
    /// </summary>
    public static class ProxyHelper
    {
        /// <summary>
        /// Reads and parses the proxy configuration file.
        /// </summary>
        public static List<string> LoadProxies(string filePath)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return list;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("proxies", out var proxiesArray) && proxiesArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in proxiesArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("proxy", out var proxyProp))
                        {
                            string val = proxyProp.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                list.Add(val);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Suppress file parsing exceptions, return empty/partial list
            }

            return list;
        }

        /// <summary>
        /// Checks a collection of proxies in parallel to find functional ones.
        /// Stops early if targetCount of alive proxies is reached.
        /// </summary>
        public static async Task<List<string>> GetAliveProxiesAsync(
            List<string> allProxies, 
            Action<string> logAction,
            int maxToCheck = 30, 
            int targetAliveCount = 3, 
            int timeoutSeconds = 3)
        {
            var aliveProxies = new List<string>();
            if (allProxies == null || allProxies.Count == 0)
            {
                return aliveProxies;
            }

            // Shuffle proxies to avoid checking the same subset every time
            var rng = new Random();
            var shuffled = allProxies.OrderBy(_ => rng.Next()).Take(maxToCheck).ToList();

            logAction($"[PROXY] Checking up to {shuffled.Count} proxies in parallel for connectivity...");

            using var cts = new CancellationTokenSource();
            using var semaphore = new SemaphoreSlim(10); // Max 10 concurrent requests
            var lockObj = new object();

            var tasks = shuffled.Select(async proxy =>
            {
                await semaphore.WaitAsync(cts.Token);
                try
                {
                    if (cts.Token.IsCancellationRequested) return;

                    bool isAlive = await TestProxyAsync(proxy, timeoutSeconds, cts.Token);
                    if (isAlive)
                    {
                        lock (lockObj)
                        {
                            if (!cts.Token.IsCancellationRequested)
                            {
                                aliveProxies.Add(proxy);
                                logAction($"[PROXY] ACTIVE: {proxy}");
                                if (aliveProxies.Count >= targetAliveCount)
                                {
                                    cts.Cancel(); // Cancel remaining checks once we have enough alive proxies
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors during check
                }
                finally
                {
                    semaphore.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Handle or ignore task cancellation exception
            }

            logAction($"[PROXY] Found {aliveProxies.Count} active proxies.");
            return aliveProxies;
        }
        /// <summary>
        /// Reads and parses proxy strings from a file.
        /// Supports JSON format (using the "proxies" key) or plain text format (one proxy per line).
        /// </summary>
        public static List<string> LoadProxiesFromFile(string filePath)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return list;
            }

            try
            {
                string extension = Path.GetExtension(filePath).ToLower();
                if (extension == ".json")
                {
                    return LoadProxies(filePath);
                }
                else
                {
                    // Plain text file, line by line
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        {
                            list.Add(trimmed);
                        }
                    }
                }
            }
            catch
            {
                // Suppress exceptions
            }

            return list;
        }

        /// <summary>
        /// Parses a proxy string into a WebProxy instance, supporting username/password credentials.
        /// Formats supported:
        /// - username:password@ip:port
        /// - ip:port:username:password
        /// - ip:port
        /// - protocol://username:password@ip:port
        /// </summary>
        public static System.Net.WebProxy? ParseProxy(string proxyStr)
        {
            if (string.IsNullOrWhiteSpace(proxyStr)) return null;

            proxyStr = proxyStr.Trim();

            try
            {
                string scheme = "http";
                string hostPort = "";
                string? username = null;
                string? password = null;

                if (proxyStr.Contains("://"))
                {
                    var uri = new Uri(proxyStr);
                    scheme = uri.Scheme;
                    hostPort = uri.Authority;
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var parts = uri.UserInfo.Split(':');
                        username = parts[0];
                        if (parts.Length > 1) password = parts[1];
                    }
                }
                else
                {
                    if (proxyStr.Contains("@"))
                    {
                        var mainParts = proxyStr.Split('@');
                        var credsPart = mainParts[0];
                        hostPort = mainParts[1];

                        var creds = credsPart.Split(':');
                        username = creds[0];
                        if (creds.Length > 1) password = creds[1];
                    }
                    else
                    {
                        var parts = proxyStr.Split(':');
                        if (parts.Length == 4)
                        {
                            // host:port:username:password format
                            hostPort = $"{parts[0]}:{parts[1]}";
                            username = parts[2];
                            password = parts[3];
                        }
                        else
                        {
                            hostPort = proxyStr;
                        }
                    }
                }

                var partsHost = hostPort.Split(':');
                var host = partsHost[0];
                int port = partsHost.Length > 1 ? int.Parse(partsHost[1]) : 80;

                var proxyUri = new UriBuilder(scheme, host) { Port = port }.Uri;
                var webProxy = new System.Net.WebProxy(proxyUri);

                if (!string.IsNullOrEmpty(username))
                {
                    webProxy.Credentials = new System.Net.NetworkCredential(username, password);
                }

                return webProxy;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> TestProxyAsync(string proxy, int timeoutSeconds, CancellationToken cancellationToken)
        {
            try
            {
                var webProxy = ParseProxy(proxy);
                if (webProxy == null) return false;

                var handler = new HttpClientHandler
                {
                    Proxy = webProxy,
                    UseProxy = true
                };

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                // Use HTTP GET or HEAD to youtube.com to test if it connects and isn't blocked.
                // We use YouTube as the test target since the ultimate goal is fetching YouTube transcripts.
                using var response = await client.GetAsync("https://www.youtube.com", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
