using System;
using System.IO;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoCreateImage
{
    public class AutomationTask : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private string _videoUrl = string.Empty;
        private string _targetLanguage = string.Empty;
        private string _voiceId = string.Empty;
        private bool _step1 = true;
        private bool _step2 = true;
        private bool _step3 = true;
        private bool _step4 = true;
        private bool _step5 = true;
        private string _status = "Pending";
        private string _selectedProfile = string.Empty;
        private string _logs = string.Empty;
        private DateTime _createdAt = DateTime.Now;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(CreatedAtFormatted)); }
        }

        public string CreatedAtFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

        public string SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); }
        }

        public string VideoUrl
        {
            get => _videoUrl;
            set
            {
                _videoUrl = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VideoId));
            }
        }

        public string TargetLanguage
        {
            get => _targetLanguage;
            set { _targetLanguage = value; OnPropertyChanged(); }
        }

        public string VoiceId
        {
            get => _voiceId;
            set { _voiceId = value; OnPropertyChanged(); }
        }

        public bool Step1
        {
            get => _step1;
            set { _step1 = value; OnPropertyChanged(); }
        }

        public bool Step2
        {
            get => _step2;
            set { _step2 = value; OnPropertyChanged(); }
        }

        public bool Step3
        {
            get => _step3;
            set { _step3 = value; OnPropertyChanged(); }
        }

        public bool Step4
        {
            get => _step4;
            set { _step4 = value; OnPropertyChanged(); }
        }

        public bool Step5
        {
            get => _step5;
            set { _step5 = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Logs
        {
            get => _logs;
            set { _logs = value; OnPropertyChanged(); }
        }

        public string VideoId => ExtractVideoId(VideoUrl);

        public string OutputDir
        {
            get
            {
                string baseDir = ConfigService.CurrentSettings.OutputsDir;
                if (string.IsNullOrEmpty(baseDir))
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    baseDir = Path.Combine(desktopPath, "Outputs");
                }
                return Path.Combine(baseDir, VideoId);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string ExtractVideoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "unknown";
            if (url.Length == 11 && Regex.IsMatch(url, @"^[a-zA-Z0-9_-]{11}$"))
            {
                return url;
            }
            var match = Regex.Match(url, @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/ ]{11})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "unknown";
        }
    }
}
