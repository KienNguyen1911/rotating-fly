using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoCreateImage
{
    public partial class MainWindow : Window
    {
        private void LogMedia(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtMediaLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                TxtMediaLogs.ScrollToEnd();
            });
        }

        private void BtnBrowseMediaAssetDir_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Chọn thư mục Assets của Task"
            };

            if (openFolderDialog.ShowDialog() == true)
            {
                string folderPath = openFolderDialog.FolderName;
                TxtMediaAssetDir.Text = folderPath;
                LogMedia($"Đã chọn thư mục Assets: {folderPath}");

                try
                {
                    // Auto-fill background image
                    string translated = Path.Combine(folderPath, "translated_thumbnail.png");
                    string cleaned = Path.Combine(folderPath, "cleaned_thumbnail.png");
                    if (File.Exists(translated))
                    {
                        TxtMediaBgImage.Text = translated;
                        LogMedia($"Tự động chọn ảnh nền dịch: {Path.GetFileName(translated)}");
                    }
                    else if (File.Exists(cleaned))
                    {
                        TxtMediaBgImage.Text = cleaned;
                        LogMedia($"Tự động chọn ảnh nền gốc sạch: {Path.GetFileName(cleaned)}");
                    }
                    else
                    {
                        var jpgFiles = Directory.GetFiles(folderPath, "*thumbnail.jpg");
                        if (jpgFiles.Length > 0)
                        {
                            TxtMediaBgImage.Text = jpgFiles[0];
                            LogMedia($"Tự động chọn ảnh nền: {Path.GetFileName(jpgFiles[0])}");
                        }
                    }

                    // Auto-fill audio
                    string voiceover = Path.Combine(folderPath, "voiceover.mp3");
                    if (File.Exists(voiceover))
                    {
                        TxtMediaVoiceover.Text = voiceover;
                        LogMedia($"Tự động chọn giọng đọc: {Path.GetFileName(voiceover)}");
                    }
                    else
                    {
                        var mp3Files = Directory.GetFiles(folderPath, "*.mp3");
                        if (mp3Files.Length > 0)
                        {
                            TxtMediaVoiceover.Text = mp3Files[0];
                            LogMedia($"Tự động chọn giọng đọc: {Path.GetFileName(mp3Files[0])}");
                        }
                    }

                    // Auto-fill SRT
                    string srt = Path.Combine(folderPath, "voiceover.srt");
                    if (File.Exists(srt))
                    {
                        TxtMediaSrtFile.Text = srt;
                        LogMedia($"Tự động chọn file phụ đề (SRT): {Path.GetFileName(srt)}");
                    }
                    else
                    {
                        var srtFiles = Directory.GetFiles(folderPath, "*.srt");
                        if (srtFiles.Length > 0)
                        {
                            TxtMediaSrtFile.Text = srtFiles[0];
                            LogMedia($"Tự động chọn file phụ đề (SRT): {Path.GetFileName(srtFiles[0])}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMedia($"[WARNING] Lỗi quét thư mục assets: {ex.Message}");
                }
            }
        }

        private void BtnBrowseMediaSrtFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Subtitle Files (*.srt)|*.srt|All files (*.*)|*.*",
                Title = "Chọn file phụ đề (SRT)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtMediaSrtFile.Text = openFileDialog.FileName;
                LogMedia($"Đã chọn file phụ đề: {Path.GetFileName(openFileDialog.FileName)}");
            }
        }

        private void BtnBrowseMediaEffectDir_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Chọn thư mục chứa hiệu ứng (Ảnh png/jpg)"
            };

            if (openFolderDialog.ShowDialog() == true)
            {
                TxtMediaEffectDir.Text = openFolderDialog.FolderName;
                LogMedia($"Đã chọn thư mục hiệu ứng: {openFolderDialog.FolderName}");
            }
        }

        private void BtnBrowseMediaBgImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*",
                Title = "Chọn ảnh nền (Background Image)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtMediaBgImage.Text = openFileDialog.FileName;
                LogMedia($"Đã chọn ảnh nền: {Path.GetFileName(openFileDialog.FileName)}");
            }
        }

        private void BtnBrowseMediaVoiceover_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Audio Files (*.mp3;*.wav;*.m4a)|*.mp3;*.wav;*.m4a|All files (*.*)|*.*",
                Title = "Chọn file voiceover (Audio)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtMediaVoiceover.Text = openFileDialog.FileName;
                LogMedia($"Đã chọn voiceover: {Path.GetFileName(openFileDialog.FileName)}");
            }
        }


        private void CbMediaMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbMediaMode == null) return;

            bool isVideo = CbMediaMode.SelectedIndex == 0;

            if (TxtMediaBgImage != null && BtnBrowseMediaBgImage != null)
            {
                TxtMediaBgImage.IsEnabled = isVideo;
                BtnBrowseMediaBgImage.IsEnabled = isVideo;
            }

            if (CbMediaResolution != null && LblMediaResolution != null)
            {
                CbMediaResolution.IsEnabled = isVideo;
                LblMediaResolution.Opacity = isVideo ? 1.0 : 0.5;
            }
        }

        private void BtnClearMediaForm_Click(object sender, RoutedEventArgs e)
        {
            TxtMediaAssetDir.Text = string.Empty;
            TxtMediaBgImage.Text = string.Empty;
            TxtMediaVoiceover.Text = string.Empty;
            TxtMediaSrtFile.Text = string.Empty;
            TxtMediaEffectDir.Text = string.Empty;
            LogMedia("Đã xóa dữ liệu trên Form.");
        }

        private async void BtnGenerateMedia_Click(object sender, RoutedEventArgs e)
        {
            bool isVideo = CbMediaMode.SelectedIndex == 0;
            string mode = isVideo ? "video" : "audio";
            string bgImage = TxtMediaBgImage.Text.Trim();
            string voiceover = TxtMediaVoiceover.Text.Trim();
            string srtFile = TxtMediaSrtFile.Text.Trim();
            string effectDir = TxtMediaEffectDir.Text.Trim();
            
            // Validation
            if (isVideo && string.IsNullOrEmpty(bgImage))
            {
                MessageBox.Show("Vui lòng chọn ảnh nền để tạo video!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(voiceover))
            {
                MessageBox.Show("Vui lòng chọn file audio/voiceover!", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save dialog
            var saveFileDialog = new SaveFileDialog
            {
                Title = isVideo ? "Lưu video kết quả" : "Lưu audio kết quả"
            };

            if (isVideo)
            {
                saveFileDialog.Filter = "Video File (*.mp4)|*.mp4|All files (*.*)|*.*";
                saveFileDialog.FileName = "final_video.mp4";
            }
            else
            {
                saveFileDialog.Filter = "Audio File (*.mp3)|*.mp3|Audio File (*.wav)|*.wav|All files (*.*)|*.*";
                saveFileDialog.FileName = "final_voiceover.mp3";
            }

            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            string outputPath = saveFileDialog.FileName;
            BtnGenerateMedia.IsEnabled = false;
            LogMedia($"Bắt đầu tiến trình xử lý media (Chế độ: {mode.ToUpper()})...");

            // Extract advanced configurations
            string resolution = "1920x1080";
            if (CbMediaResolution.SelectedItem is ComboBoxItem resItem)
            {
                string resText = resItem.Content?.ToString() ?? "";
                if (resText.Contains("4K")) resolution = "3840x2160";
            }

            await Task.Run(() => RunPythonMediaGenerator(mode, bgImage, voiceover, resolution, outputPath, srtFile, effectDir));

            BtnGenerateMedia.IsEnabled = true;
        }

        private string GetPythonExecutablePath()
        {
            try
            {
                // 1. Try local appdata programs folder (standard Python.org install path on Windows)
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programsPythonDir = Path.Combine(localAppData, @"Programs\Python");
                if (Directory.Exists(programsPythonDir))
                {
                    foreach (var pythonDir in Directory.GetDirectories(programsPythonDir, "Python*"))
                    {
                        string path = Path.Combine(pythonDir, "python.exe");
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
            }
            catch { }

            // 2. Fallback to registry or standard path resolution
            return "python";
        }

        private void RunPythonMediaGenerator(string mode, string bgImage, string voiceover, string resolution, string outputPath, string srtFile, string effectDir)
        {
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "media_generator.py");
            
            // Check if script is copied to bin/Debug folder, if not look in project directory
            if (!File.Exists(scriptPath))
            {
                // Fallback to workspace path
                scriptPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "media_generator.py"));
            }

            if (!File.Exists(scriptPath))
            {
                LogMedia($"[ERROR] Không tìm thấy file script python tại: {scriptPath}");
                return;
            }

            string pythonExe = GetPythonExecutablePath();
            LogMedia($"Đang gọi script xử lý: {scriptPath} sử dụng Python: {pythonExe}");

            string arguments = $"\"{scriptPath}\" --mode {mode} --audio \"{voiceover}\" --output \"{outputPath}\"";
            if (mode == "video")
            {
                arguments += $" --image \"{bgImage}\" --resolution {resolution}";
                if (!string.IsNullOrEmpty(srtFile) && File.Exists(srtFile))
                {
                    arguments += $" --srt \"{srtFile}\"";
                }
                if (!string.IsNullOrEmpty(effectDir) && Directory.Exists(effectDir))
                {
                    arguments += $" --effect \"{effectDir}\"";
                }
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.Environment["PYTHONUNBUFFERED"] = "1";

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogMedia(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogMedia($"[STDERR] {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        LogMedia($"[HOÀN THÀNH] Xử lý media thành công. File lưu tại: {outputPath}");
                    }
                    else
                    {
                        LogMedia($"[LỖI] Script python kết thúc với mã lỗi: {process.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMedia($"[EXCEPTION] Không thể khởi chạy tiến trình Python: {ex.Message}");
                LogMedia("[HD] Hãy chắc chắn rằng Python đã được cấu hình trong Environment Path (biến môi trường) và bạn đã cài đặt các thư viện cần thiết bằng lệnh: pip install moviepy pillow");
            }
        }
    }
}
