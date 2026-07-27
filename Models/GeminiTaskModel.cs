using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AssetAutomator.Models.Nodes;

namespace AssetAutomator.Models
{
    public class GeminiTaskModel : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _topic = "";
        private GemOptionItem? _selectedScriptwriterGem;
        private string _selectedModel = "3.6 Flash";
        private string _selectedExtension = "Tắt (Standard)";
        private bool _enableDeepResearch = true;
        private string _voiceId = "";
        private GemOptionItem? _selectedSceneCreatorGem;
        private string _selectedImageProvider = "flow_local";
        private NodeStatus _status = NodeStatus.Idle;
        private string _currentStepInfo = "Sẵn sàng";
        private string _logs = "";
        private NodeStatus _step1Status = NodeStatus.Idle;
        private NodeStatus _step2Status = NodeStatus.Idle;
        private NodeStatus _step3Status = NodeStatus.Idle;
        private NodeStatus _step4Status = NodeStatus.Idle;
        private NodeStatus _step5Status = NodeStatus.Idle;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string CreatedAtFormatted => CreatedAt.ToString("HH:mm dd/MM");

        /// <summary>
        /// Active Gemini Chat session ID maintained across Step 2A (Deep Research) and Step 2B (Transcript Generation).
        /// </summary>
        public string? ActiveSessionId { get; set; }

        /// <summary>
        /// Timestamped folder name assigned when the task starts executing.
        /// Format: dd-MM-yyyy_HH-mm (e.g. 27-07-2026_00-31).
        /// If not yet set, falls back to a sanitized topic slug.
        /// </summary>
        public string? OutputFolderName { get; set; }

        public string Logs
        {
            get => _logs;
            set { if (_logs != value) { _logs = value; OnPropertyChanged(); } }
        }

        public NodeStatus Step1Status
        {
            get => _step1Status;
            set { if (_step1Status != value) { _step1Status = value; OnPropertyChanged(); } }
        }
        public NodeStatus Step2Status
        {
            get => _step2Status;
            set { if (_step2Status != value) { _step2Status = value; OnPropertyChanged(); } }
        }
        public NodeStatus Step3Status
        {
            get => _step3Status;
            set { if (_step3Status != value) { _step3Status = value; OnPropertyChanged(); } }
        }
        public NodeStatus Step4Status
        {
            get => _step4Status;
            set { if (_step4Status != value) { _step4Status = value; OnPropertyChanged(); } }
        }
        public NodeStatus Step5Status
        {
            get => _step5Status;
            set { if (_step5Status != value) { _step5Status = value; OnPropertyChanged(); } }
        }

        public string Step1Logs { get; set; } = "";
        public string Step2Logs { get; set; } = "";
        public string Step3Logs { get; set; } = "";
        public string Step4Logs { get; set; } = "";
        public string Step5Logs { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public string Topic
        {
            get => _topic;
            set { if (_topic != value) { _topic = value; OnPropertyChanged(); } }
        }

        public GemOptionItem? SelectedScriptwriterGem
        {
            get => _selectedScriptwriterGem;
            set { if (_selectedScriptwriterGem != value) { _selectedScriptwriterGem = value; OnPropertyChanged(); } }
        }

        public string SelectedModel
        {
            get => _selectedModel;
            set { if (_selectedModel != value) { _selectedModel = value ?? "3.6 Flash"; OnPropertyChanged(); } }
        }

        public string SelectedExtension
        {
            get => _selectedExtension;
            set { if (_selectedExtension != value) { _selectedExtension = value ?? "Tắt (Standard)"; OnPropertyChanged(); } }
        }

        public bool EnableDeepResearch
        {
            get => _enableDeepResearch;
            set { if (_enableDeepResearch != value) { _enableDeepResearch = value; OnPropertyChanged(); } }
        }

        private string _targetLanguage = "";

        public string VoiceId
        {
            get => _voiceId;
            set { if (_voiceId != value) { _voiceId = value; OnPropertyChanged(); } }
        }

        public string TargetLanguage
        {
            get => _targetLanguage;
            set { if (_targetLanguage != value) { _targetLanguage = value; OnPropertyChanged(); } }
        }

        public GemOptionItem? SelectedSceneCreatorGem
        {
            get => _selectedSceneCreatorGem;
            set { if (_selectedSceneCreatorGem != value) { _selectedSceneCreatorGem = value; OnPropertyChanged(); } }
        }

        private string _characterRef = "";

        public string CharacterRef
        {
            get => _characterRef;
            set { if (_characterRef != value) { _characterRef = value; OnPropertyChanged(); } }
        }

        public string SelectedImageProvider
        {
            get => _selectedImageProvider;
            set { if (_selectedImageProvider != value) { _selectedImageProvider = value; OnPropertyChanged(); } }
        }

        public NodeStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadge));
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsSuccess));
                    OnPropertyChanged(nameof(IsFailed));
                }
            }
        }

        public string CurrentStepInfo
        {
            get => _currentStepInfo;
            set { if (_currentStepInfo != value) { _currentStepInfo = value; OnPropertyChanged(); } }
        }

        public string StatusBadge => Status switch
        {
            NodeStatus.Running => "⏳ Đang chạy...",
            NodeStatus.Success => "✔️ Hoàn thành",
            NodeStatus.Failed => "❌ Lỗi",
            _ => "Sẵn sàng"
        };

        public bool IsRunning => Status == NodeStatus.Running;
        public bool IsSuccess => Status == NodeStatus.Success;
        public bool IsFailed => Status == NodeStatus.Failed;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
