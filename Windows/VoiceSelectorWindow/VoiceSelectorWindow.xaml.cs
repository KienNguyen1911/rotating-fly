using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AutoCreateImage
{
    public partial class VoiceSelectorWindow : Window
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private int _currentPage = 0;
        private bool _hasMore = false;
        private bool _isLoading = false;

        public string SelectedVoiceId { get; private set; } = string.Empty;
        public SharedVoiceInfo? SelectedVoice { get; private set; }

        public VoiceSelectorWindow(string apiKey, string currentVoiceId)
        {
            InitializeComponent();
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            
            if (!string.IsNullOrEmpty(currentVoiceId))
            {
                TxtSelectedVoiceId.Text = currentVoiceId;
                SelectedVoiceId = currentVoiceId;
            }

            // Load initial page
            Loaded += async (s, e) => await LoadVoicesAsync();
        }

        private async Task LoadVoicesAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            OverlayStatus.Visibility = Visibility.Visible;
            TxtStatusText.Text = "Loading shared voices...";
            ProgressLoading.Visibility = Visibility.Visible;

            try
            {
                // Build Query Parameters
                var queryParams = new List<string>();

                // Page size
                int pageSize = 30;
                if (ComboPageSize.SelectedItem is ComboBoxItem selectedPageSizeItem && 
                    int.TryParse(selectedPageSizeItem.Tag?.ToString(), out int parsedSize))
                {
                    pageSize = parsedSize;
                }
                queryParams.Add($"page_size={pageSize}");

                // Page
                queryParams.Add($"page={_currentPage}");

                // Sort
                if (ComboSort.SelectedItem is ComboBoxItem selectedSortItem && selectedSortItem.Tag != null)
                {
                    queryParams.Add($"sort={selectedSortItem.Tag}");
                }

                // Gender
                if (ComboGender.SelectedItem is ComboBoxItem selectedGenderItem && 
                    selectedGenderItem.Tag != null && 
                    !string.IsNullOrEmpty(selectedGenderItem.Tag.ToString()))
                {
                    queryParams.Add($"gender={selectedGenderItem.Tag}");
                }

                // Age
                if (ComboAge.SelectedItem is ComboBoxItem selectedAgeItem && 
                    selectedAgeItem.Tag != null && 
                    !string.IsNullOrEmpty(selectedAgeItem.Tag.ToString()))
                {
                    queryParams.Add($"age={selectedAgeItem.Tag}");
                }

                // Search
                string search = TxtSearch.Text.Trim();
                if (!string.IsNullOrEmpty(search))
                {
                    queryParams.Add($"search={Uri.EscapeDataString(search)}");
                }

                // Language
                string language = TxtLanguage.Text.Trim();
                if (!string.IsNullOrEmpty(language))
                {
                    queryParams.Add($"language={Uri.EscapeDataString(language)}");
                }

                // Use Cases
                var selectedUseCases = new List<string>();
                foreach (var child in PanelUseCases.Children)
                {
                    if (child is CheckBox cb && cb.IsChecked == true && cb.Tag != null)
                    {
                        selectedUseCases.Add(cb.Tag.ToString()!);
                    }
                }
                if (selectedUseCases.Count > 0)
                {
                    string useCasesStr = string.Join(",", selectedUseCases);
                    queryParams.Add($"use_cases={Uri.EscapeDataString(useCasesStr)}");
                }

                string url = $"https://api.ai84.pro/v1/shared-voices?{string.Join("&", queryParams)}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("xi-api-key", _apiKey);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<SharedVoicesResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result != null)
                    {
                        DgridVoices.ItemsSource = result.voices;
                        _hasMore = result.has_more;
                        TxtPageIndex.Text = $"Page {_currentPage + 1}";
                        BtnPrevPage.IsEnabled = _currentPage > 0;
                        BtnNextPage.IsEnabled = _hasMore;

                        if (result.voices == null || result.voices.Count == 0)
                        {
                            OverlayStatus.Visibility = Visibility.Visible;
                            TxtStatusText.Text = "No shared voices found matching the filters.";
                            ProgressLoading.Visibility = Visibility.Collapsed;
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
                    MessageBox.Show($"Failed to fetch shared voices: {response.StatusCode}\n{errorMsg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    OverlayStatus.Visibility = Visibility.Visible;
                    TxtStatusText.Text = "Error loading voices.";
                    ProgressLoading.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while loading voices: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                OverlayStatus.Visibility = Visibility.Visible;
                TxtStatusText.Text = "Error loading voices.";
                ProgressLoading.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async void BtnApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 0;
            await LoadVoicesAsync();
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

        private async void ComboPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                _currentPage = 0;
                await LoadVoicesAsync();
            }
        }

        private void DgridVoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgridVoices.SelectedItem is SharedVoiceInfo voice)
            {
                TxtSelectedVoiceId.Text = voice.voice_id;
                SelectedVoiceId = voice.voice_id;
                SelectedVoice = voice;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedVoiceId))
            {
                MessageBox.Show("Please select a voice first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private async void FilterInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                _currentPage = 0;
                await LoadVoicesAsync();
            }
        }
    }
}
