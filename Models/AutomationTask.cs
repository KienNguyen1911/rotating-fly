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
        private bool _stepSrt = true;
        private bool _isSelected = false;
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

        public bool StepSrt
        {
            get => _stepSrt;
            set { _stepSrt = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
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

        private string _step1Status = "Pending";
        private string _step2Status = "Pending";
        private string _step3Status = "Pending";
        private string _step4Status = "Pending";
        private string _step5Status = "Pending";
        private string _stepSrtStatus = "Pending";

        public string Step1Status
        {
            get => _step1Status;
            set { _step1Status = value ?? "Pending"; OnPropertyChanged(); }
        }

        public string Step2Status
        {
            get => _step2Status;
            set { _step2Status = value ?? "Pending"; OnPropertyChanged(); }
        }

        public string Step3Status
        {
            get => _step3Status;
            set { _step3Status = value ?? "Pending"; OnPropertyChanged(); }
        }

        public string Step4Status
        {
            get => _step4Status;
            set { _step4Status = value ?? "Pending"; OnPropertyChanged(); }
        }

        public string Step5Status
        {
            get => _step5Status;
            set { _step5Status = value ?? "Pending"; OnPropertyChanged(); }
        }

        public string StepSrtStatus
        {
            get => _stepSrtStatus;
            set { _stepSrtStatus = value ?? "Pending"; OnPropertyChanged(); }
        }

        private int _srtMethod = 1;

        public int SrtMethod
        {
            get => _srtMethod;
            set
            {
                _srtMethod = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseSrtMethod1));
                OnPropertyChanged(nameof(UseSrtMethod2));
            }
        }

        public bool UseSrtMethod1
        {
            get => SrtMethod == 1;
            set { if (value) SrtMethod = 1; }
        }

        public bool UseSrtMethod2
        {
            get => SrtMethod == 2;
            set { if (value) SrtMethod = 2; }
        }

        public string VideoId => YoutubeHelper.ExtractVideoId(VideoUrl);

        public string OutputDir => YoutubeHelper.GetOutputDir(VideoId);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
