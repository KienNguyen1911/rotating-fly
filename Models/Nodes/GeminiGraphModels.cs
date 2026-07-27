using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AssetAutomator.Models.Nodes
{
    public enum NodeStatus
    {
        Idle,
        Running,
        Success,
        Failed
    }

    public class GemOptionItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public override string ToString() => Name;

        // Value equality by Id so WPF ComboBox preserves SelectedItem
        // even after AvailableGems.Clear() + re-populate with new instances.
        public override bool Equals(object? obj) =>
            obj is GemOptionItem other && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            Id?.ToLowerInvariant().GetHashCode() ?? 0;
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class GeminiConnectionViewModel : INotifyPropertyChanged
    {
        private GeminiNodeViewModel _sourceNode = null!;
        private GeminiNodeViewModel _targetNode = null!;
        private Brush _brush = new SolidColorBrush(Color.FromArgb(160, 160, 170, 185));

        public GeminiNodeViewModel SourceNode
        {
            get => _sourceNode;
            set { if (_sourceNode != value) { _sourceNode = value; OnPropertyChanged(); } }
        }

        public GeminiNodeViewModel TargetNode
        {
            get => _targetNode;
            set { if (_targetNode != value) { _targetNode = value; OnPropertyChanged(); } }
        }

        public Brush Brush
        {
            get => _brush;
            set { if (_brush != value) { _brush = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GeminiNodeViewModel : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private NodeStatus _status = NodeStatus.Idle;
        private bool _isEditable = true;
        private string _topic = "Tâm Lý Học Đêm Muộn: Tại Sao Bạn Lại Trì Hoãn Giấc Ngủ";
        private GemOptionItem? _selectedScriptwriterGem;
        private bool _enableDeepResearch = true;
        private string _voiceId = "TxGEwhxGl3yH6PtxVf7N";
        private GemOptionItem? _selectedSceneCreatorGem;
        private string _selectedImageProvider = "flow_local";

        public string Id { get; set; } = string.Empty;
        public int StepNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string HeaderColorHex { get; set; } = "#2196F3"; // Accent color hex for border / badge

        public Point InputAnchor => new Point(_x, _y + 22);
        public Point OutputAnchor => new Point(_x + 320, _y + 22);

        public Point Location
        {
            get => new Point(_x, _y);
            set
            {
                if (_x != value.X || _y != value.Y)
                {
                    _x = value.X;
                    _y = value.Y;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(X));
                    OnPropertyChanged(nameof(Y));
                    OnPropertyChanged(nameof(InputAnchor));
                    OnPropertyChanged(nameof(OutputAnchor));
                }
            }
        }

        public double X
        {
            get => _x;
            set
            {
                if (_x != value)
                {
                    _x = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Location));
                    OnPropertyChanged(nameof(InputAnchor));
                    OnPropertyChanged(nameof(OutputAnchor));
                }
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                if (_y != value)
                {
                    _y = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Location));
                    OnPropertyChanged(nameof(InputAnchor));
                    OnPropertyChanged(nameof(OutputAnchor));
                }
            }
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

        public bool IsEditable
        {
            get => _isEditable;
            set { if (_isEditable != value) { _isEditable = value; OnPropertyChanged(); } }
        }

        // Node 1 properties
        public string Topic
        {
            get => _topic;
            set { if (_topic != value) { _topic = value; OnPropertyChanged(); } }
        }

        // Node 2 properties
        public ObservableCollection<GemOptionItem> ScriptwriterGems { get; } = new();
        public GemOptionItem? SelectedScriptwriterGem
        {
            get => _selectedScriptwriterGem;
            set { if (_selectedScriptwriterGem != value) { _selectedScriptwriterGem = value; OnPropertyChanged(); } }
        }
        public bool EnableDeepResearch
        {
            get => _enableDeepResearch;
            set { if (_enableDeepResearch != value) { _enableDeepResearch = value; OnPropertyChanged(); } }
        }
        public ICommand? RefreshGemsCommand { get; set; }

        // Node 3 properties
        public string VoiceId
        {
            get => _voiceId;
            set { if (_voiceId != value) { _voiceId = value; OnPropertyChanged(); } }
        }

        // Node 4 properties
        public ObservableCollection<GemOptionItem> SceneCreatorGems { get; } = new();
        public GemOptionItem? SelectedSceneCreatorGem
        {
            get => _selectedSceneCreatorGem;
            set { if (_selectedSceneCreatorGem != value) { _selectedSceneCreatorGem = value; OnPropertyChanged(); } }
        }
        public ObservableCollection<string> ImageProviders { get; } = new() { "flow_local", "glabs" };
        public string SelectedImageProvider
        {
            get => _selectedImageProvider;
            set { if (_selectedImageProvider != value) { _selectedImageProvider = value; OnPropertyChanged(); } }
        }

        // Node 5 properties
        public ICommand? RunWorkflowCommand { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GeminiGraphViewModel : INotifyPropertyChanged
    {
        private Point _viewportLocation = new Point(0, 0);
        private double _viewportZoom = 1.0;
        private bool _isLogDrawerOpen = false;
        private bool _isBusy = false;

        public ObservableCollection<GeminiNodeViewModel> Nodes { get; } = new();
        public ObservableCollection<GeminiConnectionViewModel> Connections { get; } = new();

        public Point ViewportLocation
        {
            get => _viewportLocation;
            set { if (_viewportLocation != value) { _viewportLocation = value; OnPropertyChanged(); } }
        }

        public double ViewportZoom
        {
            get => _viewportZoom;
            set { if (_viewportZoom != value) { _viewportZoom = value; OnPropertyChanged(); } }
        }

        public bool IsLogDrawerOpen
        {
            get => _isLogDrawerOpen;
            set { if (_isLogDrawerOpen != value) { _isLogDrawerOpen = value; OnPropertyChanged(); } }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
        }

        public ICommand ToggleLogDrawerCommand { get; }
        public ICommand CloseLogDrawerCommand { get; }

        public GeminiNodeViewModel Node1 { get; private set; } = null!;
        public GeminiNodeViewModel Node2 { get; private set; } = null!;
        public GeminiNodeViewModel Node3 { get; private set; } = null!;
        public GeminiNodeViewModel Node4 { get; private set; } = null!;
        public GeminiNodeViewModel Node5 { get; private set; } = null!;

        public GeminiGraphViewModel()
        {
            ToggleLogDrawerCommand = new RelayCommand(_ => IsLogDrawerOpen = !IsLogDrawerOpen);
            CloseLogDrawerCommand = new RelayCommand(_ => IsLogDrawerOpen = false);

            InitializeDefaultGraph();
        }

        private void InitializeDefaultGraph()
        {
            // Node 1: STEP 1 - Topic / Channel (Xanh Dương #2196F3)
            Node1 = new GeminiNodeViewModel
            {
                Id = "node_1",
                StepNumber = 1,
                Title = "STEP 1: TOPIC / CHANNEL",
                Subtitle = "Chủ đề hoặc Link YouTube",
                HeaderColorHex = "#2196F3",
                X = 80,
                Y = 180
            };

            // Node 2: STEP 2 - Deep Research (Tím #9C27B0)
            Node2 = new GeminiNodeViewModel
            {
                Id = "node_2",
                StepNumber = 2,
                Title = "STEP 2: DEEP RESEARCH",
                Subtitle = "Tạo Transcript từ Gemini Gem",
                HeaderColorHex = "#9C27B0",
                X = 450,
                Y = 180
            };

            // Node 3: STEP 3 - Voiceover (Xanh Lá #4CAF50)
            Node3 = new GeminiNodeViewModel
            {
                Id = "node_3",
                StepNumber = 3,
                Title = "STEP 3: VOICEOVER & SRT",
                Subtitle = "Sinh Voice MP3 & Phụ đề SRT",
                HeaderColorHex = "#4CAF50",
                X = 820,
                Y = 180
            };

            // Node 4: STEP 4 - Scene Creator (Vàng Cam #FF9800)
            Node4 = new GeminiNodeViewModel
            {
                Id = "node_4",
                StepNumber = 4,
                Title = "STEP 4: SCENE CREATOR",
                Subtitle = "Tạo Cảnh & Sinh Ảnh Hàng Loạt",
                HeaderColorHex = "#FF9800",
                X = 1190,
                Y = 180
            };

            // Node 5: STEP 5 - Execute & Render (Hồng Đỏ #E91E63)
            Node5 = new GeminiNodeViewModel
            {
                Id = "node_5",
                StepNumber = 5,
                Title = "STEP 5: EXECUTE & RENDER",
                Subtitle = "Chạy Pipeline & Render Video",
                HeaderColorHex = "#E91E63",
                X = 1560,
                Y = 180
            };

            Nodes.Add(Node1);
            Nodes.Add(Node2);
            Nodes.Add(Node3);
            Nodes.Add(Node4);
            Nodes.Add(Node5);

            Connections.Add(new GeminiConnectionViewModel { SourceNode = Node1, TargetNode = Node2 });
            Connections.Add(new GeminiConnectionViewModel { SourceNode = Node2, TargetNode = Node3 });
            Connections.Add(new GeminiConnectionViewModel { SourceNode = Node3, TargetNode = Node4 });
            Connections.Add(new GeminiConnectionViewModel { SourceNode = Node4, TargetNode = Node5 });
        }

        public void SetAllNodesEditable(bool editable)
        {
            foreach (var node in Nodes)
            {
                node.IsEditable = editable;
            }
        }

        public void ResetAllStatuses()
        {
            foreach (var node in Nodes)
            {
                node.Status = NodeStatus.Idle;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
