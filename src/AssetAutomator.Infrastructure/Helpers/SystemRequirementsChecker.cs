using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace AssetAutomator.Infrastructure.Helpers
{
    public class SystemRequirementResult
    {
        public bool IsChromeInstalled { get; set; }
        public string ChromePath { get; set; } = string.Empty;
        public bool IsPlaywrightOk { get; set; }
        public string PlaywrightError { get; set; } = string.Empty;
        public bool IsPythonInstalled { get; set; }
        public string PythonVersion { get; set; } = string.Empty;
        public bool IsAllOk => IsChromeInstalled && IsPlaywrightOk && IsPythonInstalled;
    }

    public static class SystemRequirementsChecker
    {
        public static async Task<SystemRequirementResult> CheckAsync()
        {
            var result = new SystemRequirementResult();
            CheckChrome(result);
            await CheckPlaywrightAsync(result);
            CheckPython(result);
            return result;
        }

        private static void CheckChrome(SystemRequirementResult result)
        {
            try
            {
                string? registryPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
                registryPath ??= Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
                if (!string.IsNullOrEmpty(registryPath) && File.Exists(registryPath))
                {
                    result.IsChromeInstalled = true;
                    result.ChromePath = registryPath;
                    return;
                }

                string[] defaultPaths =
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                };
                foreach (string path in defaultPaths)
                {
                    if (File.Exists(path))
                    {
                        result.IsChromeInstalled = true;
                        result.ChromePath = path;
                        return;
                    }
                }
            }
            catch
            {
            }

            result.IsChromeInstalled = false;
        }

        private static async Task CheckPlaywrightAsync(SystemRequirementResult result)
        {
            try
            {
                using var playwright = await Playwright.CreateAsync();
                result.IsPlaywrightOk = true;
            }
            catch (Exception ex)
            {
                result.IsPlaywrightOk = false;
                result.PlaywrightError = ex.Message;
            }
        }

        private static void CheckPython(SystemRequirementResult result)
        {
            try
            {
                string pythonExe = "python";
                string[] portablePaths =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python", "python.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "python", "python.exe")
                };
                foreach (string path in portablePaths)
                {
                    if (File.Exists(path))
                    {
                        pythonExe = path;
                        break;
                    }
                }

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process != null)
                {
                    process.WaitForExit(3000);
                    string output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        result.IsPythonInstalled = true;
                        result.PythonVersion = output;
                        return;
                    }
                }
            }
            catch
            {
            }

            result.IsPythonInstalled = false;
        }
    }
}
