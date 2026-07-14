using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Playwright;

namespace AutoCreateImage
{
    public partial class MainWindow : Window
    {
        private string GetProfilesBaseDir()
        {
            string baseDir = ConfigService.CurrentSettings.ChromeProfilesDir;
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
            }
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }
            return baseDir;
        }

        private void LoadProfiles()
        {
            try
            {
                string baseDir = GetProfilesBaseDir();
                ProfileList.Clear();

                var dirs = Directory.GetDirectories(baseDir);
                foreach (var dir in dirs)
                {
                    ProfileList.Add(Path.GetFileName(dir));
                }

                Log($"Loaded {ProfileList.Count} Chrome profiles from Desktop/ChromeProfiles.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load profiles: {ex.Message}");
            }
        }

        private void BtnRefreshProfiles_Click(object sender, RoutedEventArgs e)
        {
            LoadProfiles();
        }

        private async void BtnCreateProfile_Click(object sender, RoutedEventArgs e)
        {
            string newProfileName = TxtNewProfileName.Text.Trim();
            if (string.IsNullOrEmpty(newProfileName))
            {
                Log("[ERROR] Profile name cannot be empty.");
                MessageBox.Show("Please enter a profile name (e.g. your Gmail address).", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Remove invalid characters for directory names
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                newProfileName = newProfileName.Replace(c, '_');
            }

            string baseDir = GetProfilesBaseDir();
            string profilePath = Path.Combine(baseDir, newProfileName);

            if (Directory.Exists(profilePath))
            {
                Log($"[INFO] Profile folder '{newProfileName}' already exists. Opening browser...");
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(profilePath);
                    Log($"[INFO] Created new Chrome profile folder at: {profilePath}");
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] Failed to create profile folder: {ex.Message}");
                    return;
                }
            }

            // Refresh Profile List
            LoadProfiles();

            BtnCreateProfile.IsEnabled = false;
            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        Log("[INIT PROFILE] Initializing Chrome browser context...");
                        var context = await EnsureBrowserInitializedAsync(profilePath);

                        Log("[INIT PROFILE] Opening ChatGPT (https://chatgpt.com/)...");
                        var page = await context.NewPageAsync();
                        await page.GotoAsync("https://chatgpt.com/");
                        
                        Log("[INIT PROFILE] Chrome window opened! PLEASE LOG IN TO CHATGPT MANUALLY.");
                        Log("[INIT PROFILE] Once logged in, close the browser window or click 'Close All Browsers' in the app to save.");
                    }
                    catch (Exception ex)
                    {
                        Log($"[INIT PROFILE] [ERROR] Failed to initialize Chrome: {ex.Message}");
                    }
                });
            }
            finally
            {
                BtnCreateProfile.IsEnabled = true;
            }
        }

        private async void BtnOpenProfileBrowser_Click(object sender, RoutedEventArgs e)
        {
            var selectedProfileName = LboxProfiles.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedProfileName))
            {
                MessageBox.Show("Please select a profile from the list first.", "No Profile Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string profilePath = Path.Combine(GetProfilesBaseDir(), selectedProfileName);
            BtnOpenProfileBrowser.IsEnabled = false;
            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        Log($"[INIT PROFILE] Initializing Chrome for profile: {selectedProfileName}");
                        var context = await EnsureBrowserInitializedAsync(profilePath);

                        Log("[INIT PROFILE] Opening ChatGPT...");
                        var page = await context.NewPageAsync();
                        await page.GotoAsync("https://chatgpt.com/");
                    }
                    catch (Exception ex)
                    {
                        Log($"[ERROR] Failed to launch profile browser: {ex.Message}");
                    }
                });
            }
            finally
            {
                BtnOpenProfileBrowser.IsEnabled = true;
            }
        }

        private void BtnOpenProfileFolder_Click(object sender, RoutedEventArgs e)
        {
            var selectedProfileName = LboxProfiles.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedProfileName))
            {
                MessageBox.Show("Please select a profile from the list first.", "No Profile Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string profilePath = Path.Combine(GetProfilesBaseDir(), selectedProfileName);
            if (Directory.Exists(profilePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", profilePath);
            }
            else
            {
                MessageBox.Show("Profile directory does not exist.", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            var selectedProfileName = LboxProfiles.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedProfileName))
            {
                MessageBox.Show("Please select a profile from the list first.", "No Profile Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete profile '{selectedProfileName}'? This will permanently delete its Chrome data folder.", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            string profilePath = Path.Combine(GetProfilesBaseDir(), selectedProfileName);

            // First close if active
            if (_browserContexts.TryGetValue(profilePath, out var context))
            {
                try
                {
                    await context.CloseAsync();
                }
                catch { }
                _browserContexts.TryRemove(profilePath, out _);
            }

            BtnDeleteProfile.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(profilePath))
                        {
                            Directory.Delete(profilePath, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => MessageBox.Show($"Failed to delete folder. It may be locked by Chrome. Please close Chrome and try again. Detail: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    }
                });
                LoadProfiles();
            }
            finally
            {
                BtnDeleteProfile.IsEnabled = true;
            }
        }
    }
}
