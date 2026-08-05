using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// Represents a single image item in the Batch Image Generation tab.
    /// Implements INotifyPropertyChanged for real-time WPF DataGrid and Cards binding updates.
    /// </summary>
    public class BatchImageItem : INotifyPropertyChanged
    {
        private string _status = "Waiting";
        private string _imagePath = string.Empty;
        private string _errorMessage = string.Empty;
        private string? _mediaId;
        private string? _referenceMediaId;
        private string? _flowProjectId;
        private string? _flowProjectTitle;
        private string? _flowProjectUrl;
        private DateTime? _startedAt;
        private DateTime? _finishedAt;

        private string _transcript = string.Empty;
        private string _sceneTitle = string.Empty;
        private string _aspectRatio = "16:9";
        private string _prompt = string.Empty;

        public int Index { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public string Provider { get; set; } = "flow_local"; // always Google Flow Local

        public string Prompt
        {
            get => _prompt;
            set { _prompt = value; OnPropertyChanged(); }
        }

        public string Transcript
        {
            get => _transcript;
            set { _transcript = value; OnPropertyChanged(); }
        }

        public string SceneTitle
        {
            get => string.IsNullOrEmpty(_sceneTitle) ? $"Cảnh {Index}" : _sceneTitle;
            set { _sceneTitle = value; OnPropertyChanged(); }
        }

        public string Engine { get; set; } = "flow";
        public string Model { get; set; } = "nano-banana-2";

        public string AspectRatio
        {
            get => _aspectRatio;
            set
            {
                _aspectRatio = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RatioWidth));
                OnPropertyChanged(nameof(RatioHeight));
            }
        }

        public double RatioWidth => AspectRatio switch
        {
            "9:16" => 90,
            "1:1" => 100,
            "4:3" => 120,
            "3:4" => 90,
            _ => 160 // 16:9
        };

        public double RatioHeight => AspectRatio switch
        {
            "9:16" => 160,
            "1:1" => 100,
            "4:3" => 90,
            "3:4" => 120,
            _ => 90 // 16:9
        };

        public string Upscale { get; set; } = "none";
        public DateTime EnqueuedAt { get; set; } = DateTime.Now;

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(IsDone));
            }
        }

        public bool IsGenerating => Status != null && (Status.Equals("Generating...", StringComparison.OrdinalIgnoreCase) || Status.Equals("Processing", StringComparison.OrdinalIgnoreCase));
        public bool IsDone => Status != null && Status.Equals("Done", StringComparison.OrdinalIgnoreCase);

        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public string? MediaId
        {
            get => _mediaId;
            set { _mediaId = value; OnPropertyChanged(); }
        }

        public string? ReferenceMediaId
        {
            get => _referenceMediaId;
            set { _referenceMediaId = value; OnPropertyChanged(); }
        }

        public string? FlowProjectId
        {
            get => _flowProjectId;
            set { _flowProjectId = value; OnPropertyChanged(); }
        }

        public string? FlowProjectTitle
        {
            get => _flowProjectTitle;
            set { _flowProjectTitle = value; OnPropertyChanged(); }
        }

        public string? FlowProjectUrl
        {
            get => _flowProjectUrl;
            set { _flowProjectUrl = value; OnPropertyChanged(); }
        }

        public DateTime? StartedAt
        {
            get => _startedAt;
            set { _startedAt = value; OnPropertyChanged(); }
        }

        public DateTime? FinishedAt
        {
            get => _finishedAt;
            set { _finishedAt = value; OnPropertyChanged(); }
        }

        public string IndexFormatted => Index.ToString();
        public string CreatedTimeFormatted => EnqueuedAt.ToString("HH:mm:ss");
        public string FinishedTimeFormatted => FinishedAt?.ToString("HH:mm:ss") ?? string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}