using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace AssetAutomator
{
    public class SystemRequirementResult
    {
        public bool IsChromeInstalled { get; set; }
        public string ChromePath { get; set; } = string.Empty;
        public bool IsPlaywrightOk { get; set; }
        public string PlaywrightError { get; set; } = string.Empty;
        public bool IsPythonInstalled { get; set; }
        public string PythonVersion { get; set; } = string.Empty;
        public bool IsMoviePyInstalled { get; set; }
        public bool IsPillowInstalled { get; set; }

        public bool IsAllOk => IsChromeInstalled && IsPlaywrightOk && IsPythonInstalled && IsMoviePyInstalled && IsPillowInstalled;
    }

    public static class SystemRequirementsChecker
    {
        public static async Task<SystemRequirementResult> CheckAsync()
        {
            var result = new SystemRequirementResult();

            // 1. Check Google Chrome
            CheckChrome(result);

            // 2. Check Playwright
            await CheckPlaywrightAsync(result);

            // 3. Check Python and Libraries
            CheckPython(result);

            return result;
        }

        private static void CheckChrome(SystemRequirementResult result)
        {
            try
            {
                // Check registry
                string? regPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
                if (string.IsNullOrEmpty(regPath))
                {
                    regPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
                }

                if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath))
                {
                    result.IsChromeInstalled = true;
                    result.ChromePath = regPath;
                    return;
                }

                // Check default paths
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
                // Test initializing Playwright
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
                // Check python version
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
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

                        // Check Python modules: moviepy and Pillow (PIL)
                        CheckPythonModules(result);
                        return;
                    }
                }
            }
            catch
            {
                // Python not in PATH or not installed
            }

            result.IsPythonInstalled = false;
        }

        private static void CheckPythonModules(SystemRequirementResult result)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "-c \"import moviepy; print('moviepy_ok')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(4000);
                    string output = proc.StandardOutput.ReadToEnd();
                    result.IsMoviePyInstalled = output.Contains("moviepy_ok");
                }
            }
            catch
            {
                result.IsMoviePyInstalled = false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "-c \"import PIL; print('pillow_ok')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(4000);
                    string output = proc.StandardOutput.ReadToEnd();
                    result.IsPillowInstalled = output.Contains("pillow_ok");
                }
            }
            catch
            {
                result.IsPillowInstalled = false;
            }
        }
    }
}
