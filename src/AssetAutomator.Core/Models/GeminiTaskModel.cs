using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssetAutomator.Core.Models
{
    public class GeminiTaskModel : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _topic = "Sunday Scaries";
        private GemOptionItem? _selectedScriptwriterGem;
        private string _scriptwriterModel = "gemini-3-flash";
        private string _sceneCreatorModel = "gemini-3-flash";
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
            set { if (_step1Status != value) { _step1Status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ActiveStep)); } }
        }
        public NodeStatus Step2Status
        {
            get => _step2Status;
            set { if (_step2Status != value) { _step2Status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ActiveStep)); } }
        }
        public NodeStatus Step3Status
        {
            get => _step3Status;
            set { if (_step3Status != value) { _step3Status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ActiveStep)); } }
        }
        public NodeStatus Step4Status
        {
            get => _step4Status;
            set { if (_step4Status != value) { _step4Status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ActiveStep)); } }
        }
        public NodeStatus Step5Status
        {
            get => _step5Status;
            set { if (_step5Status != value) { _step5Status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ActiveStep)); } }
        }

        private string _step1Logs = "";
        private string _step2Logs = "";
        private string _step3Logs = "";
        private string _step4Logs = "";
        private string _step5Logs = "";

        public string Step1Logs
        {
            get => _step1Logs;
            set { if (_step1Logs != value) { _step1Logs = value; OnPropertyChanged(); } }
        }
        public string Step2Logs
        {
            get => _step2Logs;
            set { if (_step2Logs != value) { _step2Logs = value; OnPropertyChanged(); } }
        }
        public string Step3Logs
        {
            get => _step3Logs;
            set { if (_step3Logs != value) { _step3Logs = value; OnPropertyChanged(); } }
        }
        public string Step4Logs
        {
            get => _step4Logs;
            set { if (_step4Logs != value) { _step4Logs = value; OnPropertyChanged(); } }
        }
        public string Step5Logs
        {
            get => _step5Logs;
            set { if (_step5Logs != value) { _step5Logs = value; OnPropertyChanged(); } }
        }

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
            set { if (_selectedScriptwriterGem != value) { _selectedScriptwriterGem = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScriptwriterSummary)); } }
        }

        /// <summary>
        /// AI Model for Scriptwriter Gem. Holds the full model name directly
        /// (e.g. "gemini-3-pro", "gemini-3-flash-thinking-advanced").
        /// All 9 models from constants.py are available in the dropdown.
        /// </summary>
        public string ScriptwriterModel
        {
            get => _scriptwriterModel;
            set { if (_scriptwriterModel != value) { _scriptwriterModel = value ?? "gemini-3-flash"; OnPropertyChanged(); OnPropertyChanged(nameof(ScriptwriterSummary)); } }
        }

        /// <summary>
        /// AI Model for Scene Creator Gem. Holds the full model name directly
        /// (e.g. "gemini-3-pro", "gemini-3-flash-thinking-advanced").
        /// </summary>
        public string SceneCreatorModel
        {
            get => _sceneCreatorModel;
            set { if (_sceneCreatorModel != value) { _sceneCreatorModel = value ?? "gemini-3-flash"; OnPropertyChanged(); OnPropertyChanged(nameof(SceneCreatorSummary)); } }
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
            set { if (_selectedSceneCreatorGem != value) { _selectedSceneCreatorGem = value; OnPropertyChanged(); OnPropertyChanged(nameof(SceneCreatorSummary)); } }
        }

        private string _characterRef = "";

        public string CharacterRef
        {
            get => _characterRef;
            set { if (_characterRef != value) { _characterRef = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// YouTube channel URL for topic suggestions (Step 1).
        /// </summary>
        private string _channelUrl = "";
        public string ChannelUrl
        {
            get => _channelUrl;
            set { if (_channelUrl != value) { _channelUrl = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// List of suggested topics from Step 1 analysis.
        /// </summary>
        private List<SuggestedTopic> _suggestedTopics = new();
        public List<SuggestedTopic> SuggestedTopics
        {
            get => _suggestedTopics;
            set { _suggestedTopics = value ?? new(); OnPropertyChanged(); OnPropertyChanged(nameof(HasSuggestions)); }
        }

        /// <summary>
        /// Whether topic suggestions are currently loading.
        /// </summary>
        private bool _isLoadingSuggestions;
        public bool IsLoadingSuggestions
        {
            get => _isLoadingSuggestions;
            set { _isLoadingSuggestions = value; OnPropertyChanged(); }
        }

        public bool HasSuggestions => SuggestedTopics.Count > 0;

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

        // ── Row Details / Accordion support ──
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Progress percentage: 0–100 based on completed steps (1-5).
        /// </summary>
        public int ProgressPercent
        {
            get
            {
                int done = 0;
                if (Step1Status == NodeStatus.Success) done++;
                if (Step2Status == NodeStatus.Success) done++;
                if (Step3Status == NodeStatus.Success) done++;
                if (Step4Status == NodeStatus.Success) done++;
                if (Step5Status == NodeStatus.Success) done++;
                return done * 20;
            }
        }

        public int ActiveStep
        {
            get
            {
                if (Step1Status == NodeStatus.Running) return 1;
                if (Step2Status == NodeStatus.Running) return 2;
                if (Step3Status == NodeStatus.Running) return 3;
                if (Step4Status == NodeStatus.Running) return 4;
                if (Step5Status == NodeStatus.Running) return 5;
                if (Status == NodeStatus.Success) return 5;
                return 0;
            }
        }

        public string ProgressText => $"{ProgressPercent}%" + (ActiveStep > 0 ? $" (Bước {ActiveStep}/5)" : "");

        /// <summary>
        /// Quick config summary: "GemName · Model 🧠"
        /// 🧠 = model contains "pro" (Pro models have built-in thinking).
        /// </summary>
        public string ScriptwriterSummary =>
            $"{(SelectedScriptwriterGem?.Name ?? "Mặc Định")} · {ScriptwriterModel}" +
            (ScriptwriterModel.Contains("pro", StringComparison.OrdinalIgnoreCase) ? " 🧠" : "");

        public string SceneCreatorSummary =>
            $"{(SelectedSceneCreatorGem?.Name ?? "Mặc Định")} · {SceneCreatorModel}" +
            (SceneCreatorModel.Contains("pro", StringComparison.OrdinalIgnoreCase) ? " 🧠" : "");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
