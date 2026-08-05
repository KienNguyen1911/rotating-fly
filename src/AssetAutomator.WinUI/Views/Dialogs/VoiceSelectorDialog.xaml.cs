using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Steps;

namespace AssetAutomator.WinUI.Views.Dialogs;

public class VoiceModel
{
    public string VoiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Gender { get; set; } = "Female";
    public string Language { get; set; } = "en-US";
}

public sealed partial class VoiceSelectorDialog : ContentDialog
{
    private readonly string _apiKey = string.Empty;
    private readonly IHttpClientFactory? _httpClientFactory;
    private int _currentPage = 0;
    private bool _hasMore = false;
    private bool _isLoading = false;

    public string SelectedVoiceId { get; private set; } = string.Empty;
    public SharedVoiceInfo? SelectedVoice { get; private set; }

    public VoiceSelectorDialog(string apiKey, string currentVoiceId)
    {
        InitializeComponent();
        _apiKey = apiKey;
        if (string.IsNullOrEmpty(_apiKey))
        {
            var configService = App.Services.GetService<IConfigService>();
            _apiKey = configService?.CurrentSettings.Ai84ApiKey ?? string.Empty;
        }

        // Use the shared, resilience-protected "ai84" HTTP client when available so
        // a single slow / failing AI84 lookup no longer hangs the dialog for ~28 hours
        // (the previous `new HttpClient()` default timeout) — it now fails fast after
        // 60s per attempt and benefits from Polly retry + circuit-breaker.
        _httpClientFactory = App.Services?.GetService<IHttpClientFactory>();

        SelectedVoiceId = currentVoiceId;
        if (!string.IsNullOrEmpty(currentVoiceId))
        {
            TxtSelectedVoiceId.Text = currentVoiceId;
        }

        AdjustDialogWidthToScreen();
        Loaded += async (s, e) =>
        {
            AdjustDialogWidthToScreen();
            await LoadVoicesAsync();
        };
    }

    private void AdjustDialogWidthToScreen()
    {
        try
        {
            this.HorizontalAlignment = HorizontalAlignment.Center;
            this.VerticalAlignment = VerticalAlignment.Center;
            this.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            this.VerticalContentAlignment = VerticalAlignment.Stretch;

            if (RootGrid != null)
            {
                RootGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                RootGrid.VerticalAlignment = VerticalAlignment.Stretch;
                RootGrid.Width = double.NaN;
            }

            if (App.MainWindowInstance != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    double targetWidth = Math.Max(800, displayArea.WorkArea.Width * 0.50);
                    this.Resources["ContentDialogMaxWidth"] = targetWidth;
                    this.Resources["ContentDialogMinWidth"] = targetWidth;
                }
            }

            TopFilterGrid?.InvalidateMeasure();
            TopFilterGrid?.InvalidateArrange();
            RootGrid?.InvalidateMeasure();
            RootGrid?.InvalidateArrange();
            this.UpdateLayout();
        }
        catch
        {
            double fallbackWidth = 960;
            this.Resources["ContentDialogMaxWidth"] = fallbackWidth;
            this.Resources["ContentDialogMinWidth"] = fallbackWidth;
        }
    }

    public VoiceSelectorDialog(string currentVoiceId = "")
        : this(App.Services.GetService<IConfigService>()?.CurrentSettings.Ai84ApiKey ?? string.Empty, currentVoiceId)
    {
    }

    private async Task LoadVoicesAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            OverlayStatus.Visibility = Visibility.Visible;
            ProgressLoading.IsActive = false;
            ProgressLoading.Visibility = Visibility.Collapsed;
            TxtStatusText.Text = "⚠️ Chưa có AI84 API Key!\nVui lòng vào Cài đặt (Settings) và nhập AI84 API Key trước khi sử dụng.";
            _isLoading = false;
            return;
        }

        OverlayStatus.Visibility = Visibility.Visible;
        ProgressLoading.IsActive = true;
        ProgressLoading.Visibility = Visibility.Visible;
        TxtStatusText.Text = "Đang tải danh sách giọng đọc từ AI84...";

        try
        {
            var queryParams = new List<string>();

            int pageSize = 30;
            if (ComboPageSize.SelectedItem is ComboBoxItem selectedPageSizeItem &&
                int.TryParse(selectedPageSizeItem.Content?.ToString(), out int parsedSize))
            {
                pageSize = parsedSize;
            }
            queryParams.Add($"page_size={pageSize}");
            queryParams.Add($"page={_currentPage}");

            if (ComboSort.SelectedItem is ComboBoxItem selectedSortItem && selectedSortItem.Tag != null)
            {
                string sortVal = selectedSortItem.Tag.ToString()!;
                if (!string.IsNullOrEmpty(sortVal))
                {
                    queryParams.Add($"sort={sortVal}");
                }
            }

            if (ComboGender.SelectedItem is ComboBoxItem selectedGenderItem && selectedGenderItem.Tag != null)
            {
                string genderVal = selectedGenderItem.Tag.ToString()!;
                if (!string.IsNullOrEmpty(genderVal))
                {
                    queryParams.Add($"gender={genderVal}");
                }
            }

            string search = TxtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                queryParams.Add($"search={Uri.EscapeDataString(search)}");
            }

            string lang = TxtLanguage.Text.Trim();
            if (!string.IsNullOrEmpty(lang))
            {
                queryParams.Add($"language={Uri.EscapeDataString(lang)}");
            }

            string url = $"https://api.ai84.pro/v1/shared-voices?{string.Join("&", queryParams)}";

            // In-memory 5-minute cache so flip-flopping filters doesn't hit AI84 every time.
            if (TryGetCachedVoiceResponse(url, out var cachedResult))
            {
                ApplyVoicesResponse(cachedResult, search);
                return;
            }

            var http = _httpClientFactory is not null
                ? _httpClientFactory.CreateClient(VoiceoverGenerationStep.Ai84HttpClientName)
                : new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("xi-api-key", _apiKey);

            var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<SharedVoicesResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result != null)
                {
                    StoreCachedVoiceResponse(url, result);
                    ApplyVoicesResponse(result, search);
                }
            }
            else
            {
                string errorMsg = await response.Content.ReadAsStringAsync();
                OverlayStatus.Visibility = Visibility.Visible;
                ProgressLoading.IsActive = false;
                ProgressLoading.Visibility = Visibility.Collapsed;
                TxtStatusText.Text = $"⚠️ Lỗi kết nối AI84 (Mã {(int)response.StatusCode}):\n{errorMsg}";
            }
        }
        catch (Exception ex)
        {
            OverlayStatus.Visibility = Visibility.Visible;
            ProgressLoading.IsActive = false;
            ProgressLoading.Visibility = Visibility.Collapsed;
            TxtStatusText.Text = $"⚠️ Lỗi phát sinh khi tải giọng đọc:\n{ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Apply a deserialized <see cref="SharedVoicesResponse"/> to the ListView + status
    /// overlay. Extracted so cached results can also hit it.
    /// </summary>
    private void ApplyVoicesResponse(SharedVoicesResponse result, string search)
    {
        if (result.voices == null) return;

        _hasMore = result.has_more;
        TxtPageIndex.Text = $"Trang {_currentPage + 1}";
        BtnPrevPage.IsEnabled = _currentPage > 0;
        BtnNextPage.IsEnabled = _hasMore;

        var voiceModels = result.voices.Select(v => new VoiceModel
        {
            VoiceId = v.voice_id,
            Name = v.name,
            Category = string.IsNullOrWhiteSpace(v.category) ? "ElevenLabs" : v.category,
            Gender = string.IsNullOrWhiteSpace(v.gender) ? "Unspecified" : v.gender,
            Language = string.IsNullOrWhiteSpace(v.language) ? "Global" : v.language,
            Description = string.IsNullOrWhiteSpace(v.description) ? $"Voice ID: {v.voice_id}" : v.description
        }).ToList();

        // If user searched a custom ID not in list, add it as fallback
        if (voiceModels.Count == 0 && !string.IsNullOrWhiteSpace(search))
        {
            voiceModels.Add(new VoiceModel
            {
                VoiceId = search,
                Name = search,
                Category = "Tùy chọn",
                Gender = "Auto",
                Language = "Custom",
                Description = $"Giọng đọc tùy chỉnh nhập theo tên/ID '{search}'"
            });
        }

        LstVoices.ItemsSource = voiceModels;

        if (voiceModels.Count == 0)
        {
            OverlayStatus.Visibility = Visibility.Visible;
            ProgressLoading.IsActive = false;
            ProgressLoading.Visibility = Visibility.Collapsed;
            TxtStatusText.Text = "Không tìm thấy giọng đọc nào phù hợp với bộ lọc.";
        }
        else
        {
            OverlayStatus.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Process-wide cache for AI84 /v1/shared-voices responses keyed by the full query URL.
    /// Bounded by 64 entries with a 5-minute TTL so changing filters back and forth doesn't
    /// hit AI84 every time but the dialog still picks up new voices opened by other clients
    /// within the same session.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedVoiceResponse> SharedVoicesCache = new();
    private static readonly TimeSpan VoiceCacheTtl = TimeSpan.FromMinutes(5);
    private const int VoiceCacheMaxEntries = 64;

    private sealed class CachedVoiceResponse
    {
        public SharedVoicesResponse Value { get; init; } = null!;
        public DateTime ExpiresAt { get; init; }
    }

    private static bool TryGetCachedVoiceResponse(string url, out SharedVoicesResponse response)
    {
        if (SharedVoicesCache.TryGetValue(url, out var cached))
        {
            if (cached.ExpiresAt > DateTime.UtcNow)
            {
                response = cached.Value;
                return true;
            }
            SharedVoicesCache.TryRemove(url, out _);
        }
        response = null!;
        return false;
    }

    private static void StoreCachedVoiceResponse(string url, SharedVoicesResponse value)
    {
        // Simple bounded cache: drop oldest insertion if we're at the cap. The cache is
        // shared across all VoiceSelectorDialog instances in the process, so the cap protects
        // against unbounded growth from many filter permutations.
        if (SharedVoicesCache.Count >= VoiceCacheMaxEntries)
        {
            // Remove any expired entry first; otherwise drop one arbitrary entry.
            var expired = SharedVoicesCache.FirstOrDefault(kv => kv.Value.ExpiresAt <= DateTime.UtcNow);
            if (!string.IsNullOrEmpty(expired.Key))
            {
                SharedVoicesCache.TryRemove(expired.Key, out _);
            }
            else
            {
                var any = SharedVoicesCache.FirstOrDefault();
                if (!string.IsNullOrEmpty(any.Key))
                    SharedVoicesCache.TryRemove(any.Key, out _);
            }
        }

        SharedVoicesCache[url] = new CachedVoiceResponse
        {
            Value = value,
            ExpiresAt = DateTime.UtcNow + VoiceCacheTtl
        };
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            _currentPage = 0;
            await LoadVoicesAsync();
        }
    }

    private async void ComboPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            _currentPage = 0;
            await LoadVoicesAsync();
        }
    }

    private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            await LoadVoicesAsync();
        }
    }

    private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_hasMore)
        {
            _currentPage++;
            await LoadVoicesAsync();
        }
    }

    /// <summary>
    /// Run the current filter inputs against AI84. Shared by Enter-key in the search box
    /// and the explicit "🔍 Tìm kiếm" button so users always have a visible affordance.
    /// </summary>
    private async Task RunSearchAsync()
    {
        if (_isLoading) return;
        _currentPage = 0; // Always restart from page 0 when the user changes the filter.
        await LoadVoicesAsync();
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void FilterInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true; // Don't let Enter beep or trigger other enter handlers.
            await RunSearchAsync();
        }
    }

    private void LstVoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstVoices.SelectedItem is VoiceModel voice)
        {
            SelectedVoiceId = voice.VoiceId;
            SelectedVoice = new SharedVoiceInfo
            {
                voice_id = voice.VoiceId,
                name = voice.Name,
                category = voice.Category,
                gender = voice.Gender,
                language = voice.Language,
                description = voice.Description
            };
            if (TxtSelectedVoiceId != null) TxtSelectedVoiceId.Text = $"{voice.Name} ({voice.VoiceId})";
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(SelectedVoiceId))
        {
            args.Cancel = true;
        }
    }
}
