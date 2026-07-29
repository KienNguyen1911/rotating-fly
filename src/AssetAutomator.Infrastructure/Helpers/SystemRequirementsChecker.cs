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
                string? regPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
                if (string.IsNullOrEmpty(regPath))
                    regPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;

                if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath))
                {
                    result.IsChromeInstalled = true;
                    result.ChromePath = regPath;
                    return;
                }

                string[] defaultPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                };

                foreach (var path in defaultPaths)
                {
                    if (File.Exists(path))
                    {
                        result.IsChromeInstalled = true;
                        result.ChromePath = path;
                        return;
                    }
                }

                result.IsChromeInstalled = false;
            }
            catch
            {
                result.IsChromeInstalled = false;
            }
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
                string[] portablePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python", "python.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "python", "python.exe")
                };

                foreach (var path in portablePaths)
                {
                    if (File.Exists(path))
                    {
                        pythonExe = path;
                        break;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(3000);
                    string output = (proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd()).Trim();
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        result.IsPythonInstalled = true;
                        result.PythonVersion = output;
                        return;
                    }
                }
            }
            catch { }

            result.IsPythonInstalled = false;
        }
    }
}
