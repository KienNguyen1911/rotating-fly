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
    private readonly HttpClient _httpClient;
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

        _httpClient = new HttpClient();
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

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("xi-api-key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<SharedVoicesResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result != null && result.voices != null)
                {
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

    private async void FilterInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _currentPage = 0;
            await LoadVoicesAsync();
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
