using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

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
        private bool _watermarkRemoved;
        private string? _watermarkNote;
        private long _imageCacheVersion;

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
            set { _imagePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(ImageCacheKey)); }
        }

        /// <summary>
        /// Computed property: <c>ImagePath</c> joined with the
        /// <see cref="ImageCacheVersion"/> as a query string. The XAML
        /// converter binds to this so every bump of the version produces
        /// a different string, which forces <c>x:Bind OneWay</c> to
        /// re-evaluate and the underlying BitmapImage to reload from
        /// disk (WinUI keys decoded pixels on URI identity).
        /// </summary>
        public string ImageCacheKey
        {
            get
            {
                if (string.IsNullOrEmpty(_imagePath)) return string.Empty;
                return _imagePath + "?v=" + _imageCacheVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Monotonic counter that bumps every time the file at
        /// <see cref="ImagePath"/> is rewritten in-place (Gemini
        /// watermark removal, etc.). Pair with <see cref="ImageCacheKey"/>
        /// for the actual cache-bust.
        /// </summary>
        public long ImageCacheVersion
        {
            get => _imageCacheVersion;
            set
            {
                if (_imageCacheVersion == value) return;
                _imageCacheVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageCacheKey));
            }
        }

        /// <summary>
        /// Bumps <see cref="ImageCacheVersion"/> to force a UI reload of
        /// the thumbnail. Safe to call multiple times; each call produces
        /// a new value. Returns the new version.
        /// </summary>
        public long BumpImageCacheVersion()
        {
            long next = unchecked(++_imageCacheVersion);
            OnPropertyChanged(nameof(ImageCacheVersion));
            OnPropertyChanged(nameof(ImageCacheKey));
            return next;
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

        /// <summary>
        /// True if the Gemini watermark has been successfully removed from
        /// <see cref="ImagePath"/>. False means the image on disk still has
        /// the watermark (either user disabled the option, removal failed,
        /// or the image isn't a Gemini output).
        /// </summary>
        public bool WatermarkRemoved
        {
            get => _watermarkRemoved;
            set
            {
                _watermarkRemoved = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WatermarkBadgeText));
                OnPropertyChanged(nameof(HasWatermarkBadge));
            }
        }

        /// <summary>
        /// Human-readable status note. Set on success ("exact, 42ms") or
        /// failure ("Node.js chưa được cài"). Empty when no removal was attempted.
        /// </summary>
        public string? WatermarkNote
        {
            get => _watermarkNote;
            set
            {
                _watermarkNote = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WatermarkBadgeText));
            }
        }

        /// <summary>
        /// Badge text shown in the UI. "✨ Clean" when removed, "💧" when the
        /// image still has watermark, "──" when no removal was attempted.
        /// </summary>
        public string WatermarkBadgeText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ImagePath)) return "──";
                if (WatermarkRemoved) return "✨ Clean";
                if (!string.IsNullOrWhiteSpace(WatermarkNote)) return "💧";
                return "💧";
            }
        }

        /// <summary>
        /// True when the badge should be rendered (i.e. there's an image on disk
        /// and a removal result to show).
        /// </summary>
        public bool HasWatermarkBadge => !string.IsNullOrWhiteSpace(ImagePath);

        /// <summary>
        /// Trimmed tooltip for the badge — falls back to "Chưa xử lý" when
        /// no watermark state was ever computed.
        /// </summary>
        public string WatermarkTooltip => WatermarkRemoved
            ? $"Watermark removed. {WatermarkNote}"
            : (!string.IsNullOrWhiteSpace(WatermarkNote)
                ? WatermarkNote
                : "Watermark state chưa được kiểm tra");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Compiled once at type-init — matches anything like "Scene #1", "scene_001",
        // "Cảnh 1", "1", etc. Used to extract the scene number from legacy titles
        // so we can normalize them to the new compact "001" format.
        private static readonly Regex LegacyTitleNumberRegex = new Regex(
            @"\d+",
            RegexOptions.Compiled);

        /// <summary>
        /// Normalizes any legacy or user-typed SceneTitle into the current compact
        /// format — a 3-digit zero-padded scene number (e.g. "001", "042").
        ///
        /// Examples (for <paramref name="index"/> = 7):
        ///   "Scene #7: scene_007"        → "007"
        ///   "Cảnh 7"                      → "007"
        ///   "scene_007"                   → "007"
        ///   "7"                           → "007"
        ///   "" / null                     → "007"
        /// </summary>
        /// <remarks>
        /// We always fall back to <paramref name="index"/> so the title is never
        /// empty, even if the persisted value is corrupted or belongs to a totally
        /// different language/locale.
        /// </remarks>
        public static string NormalizeSceneTitle(string? rawTitle, int index)
        {
            // Default to index when the raw title is empty so we never display
            // a blank badge even if the persisted value is corrupted.
            int number = index;
            if (!string.IsNullOrWhiteSpace(rawTitle))
            {
                var match = LegacyTitleNumberRegex.Match(rawTitle);
                if (match.Success && int.TryParse(match.Value, out int parsed))
                {
                    number = parsed;
                }
            }
            return number.ToString("D3");
        }
    }
}