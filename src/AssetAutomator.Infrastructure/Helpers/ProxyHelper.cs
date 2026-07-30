using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AssetAutomator.Infrastructure.Helpers
{
    public static class ProxyHelper
    {
        public static List<string> LoadProxies(string filePath)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return list;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
                if (doc.RootElement.TryGetProperty("proxies", out var proxiesArray) && proxiesArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in proxiesArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("proxy", out var proxyProp))
                        {
                            string value = proxyProp.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(value))
                                list.Add(value);
                        }
                    }
                }
            }
            catch
            {
            }

            return list;
        }

        public static async Task<List<string>> GetAliveProxiesAsync(
            List<string> allProxies,
            Action<string> logAction,
            int maxToCheck = 30,
            int targetAliveCount = 3,
            int timeoutSeconds = 3)
        {
            var aliveProxies = new List<string>();
            if (allProxies == null || allProxies.Count == 0)
                return aliveProxies;

            var shuffled = allProxies.OrderBy(_ => Random.Shared.Next()).Take(maxToCheck).ToList();
            logAction($"[PROXY] Checking up to {shuffled.Count} proxies in parallel for connectivity...");

            using var cts = new CancellationTokenSource();
            using var semaphore = new SemaphoreSlim(10);
            var lockObj = new object();
            var tasks = shuffled.Select(async proxy =>
            {
                await semaphore.WaitAsync(cts.Token);
                try
                {
                    if (cts.Token.IsCancellationRequested)
                        return;

                    if (await TestProxyAsync(proxy, timeoutSeconds, cts.Token))
                    {
                        lock (lockObj)
                        {
                            if (!cts.Token.IsCancellationRequested)
                            {
                                aliveProxies.Add(proxy);
                                logAction($"[PROXY] ACTIVE: {proxy}");
                                if (aliveProxies.Count >= targetAliveCount)
                                    cts.Cancel();
                            }
                        }
                    }
                }
                catch
                {
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
            }

            logAction($"[PROXY] Found {aliveProxies.Count} active proxies.");
            return aliveProxies;
        }

        public static List<string> LoadProxiesFromFile(string filePath)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return list;

            try
            {
                if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    return LoadProxies(filePath);

                foreach (var line in File.ReadAllLines(filePath))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        list.Add(trimmed);
                }
            }
            catch
            {
            }

            return list;
        }

        public static System.Net.WebProxy? ParseProxy(string proxyStr)
        {
            if (string.IsNullOrWhiteSpace(proxyStr))
                return null;

            proxyStr = proxyStr.Trim();
            try
            {
                string scheme = "http";
                string hostPort;
                string? username = null;
                string? password = null;

                if (proxyStr.Contains("://"))
                {
                    var uri = new Uri(proxyStr);
                    scheme = uri.Scheme;
                    hostPort = uri.Authority;
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var credentials = uri.UserInfo.Split(':');
                        username = credentials[0];
                        if (credentials.Length > 1)
                            password = credentials[1];
                    }
                }
                else if (proxyStr.Contains('@'))
                {
                    var mainParts = proxyStr.Split('@');
                    var credentials = mainParts[0].Split(':');
                    hostPort = mainParts[1];
                    username = credentials[0];
                    if (credentials.Length > 1)
                        password = credentials[1];
                }
                else
                {
                    var parts = proxyStr.Split(':');
                    if (parts.Length == 4)
                    {
                        hostPort = $"{parts[0]}:{parts[1]}";
                        username = parts[2];
                        password = parts[3];
                    }
                    else
                    {
                        hostPort = proxyStr;
                    }
                }

                var hostParts = hostPort.Split(':');
                var proxyUri = new UriBuilder(scheme, hostParts[0])
                {
                    Port = hostParts.Length > 1 ? int.Parse(hostParts[1]) : 80
                }.Uri;
                var webProxy = new System.Net.WebProxy(proxyUri);
                if (!string.IsNullOrEmpty(username))
                    webProxy.Credentials = new System.Net.NetworkCredential(username, password);
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
                if (webProxy == null)
                    return false;

                using var client = new HttpClient(new HttpClientHandler { Proxy = webProxy, UseProxy = true })
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };
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
