using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Views.Dialogs;

public class VoiceModel
{
    public string VoiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Gender { get; set; } = "Female";
}

public sealed partial class VoiceSelectorDialog : ContentDialog
{
    public string SelectedVoiceId { get; private set; } = string.Empty;
    private List<VoiceModel> _allVoices = new();
    private List<VoiceModel> _filteredVoices = new();
    private int _currentPage = 1;
    private int _pageSize = 30;

    public VoiceSelectorDialog(string currentVoiceId = "")
    {
        InitializeComponent();
        SelectedVoiceId = currentVoiceId;
        if (!string.IsNullOrEmpty(currentVoiceId))
        {
            TxtSelectedVoiceId.Text = currentVoiceId;
        }
        LoadVoicesMock();
        ApplyFilterAndPagination();
    }

    private void LoadVoicesMock()
    {
        _allVoices = new List<VoiceModel>
        {
            new VoiceModel { VoiceId = "21m00Tcm4TlvDq8ikWAM", Name = "Rachel", Category = "conversational", Gender = "Female", Description = "Calm and professional female voice for narration" },
            new VoiceModel { VoiceId = "AZnzlk1XvdvUeBnXmlld", Name = "Domi", Category = "emotive", Gender = "Female", Description = "Strong and engaging female voice for commercials" },
            new VoiceModel { VoiceId = "EXAVITQu4vr4xnSDxMaL", Name = "Bella", Category = "conversational", Gender = "Female", Description = "Soft and friendly young female voice" },
            new VoiceModel { VoiceId = "ErXwobaYiN019PkySvjV", Name = "Antoni", Category = "conversational", Gender = "Male", Description = "Well-rounded male voice for stories and audiobooks" },
            new VoiceModel { VoiceId = "MF3mGyEYCl7XYWbV9V6O", Name = "Elli", Category = "conversational", Gender = "Female", Description = "Emotional and expressive young female voice" },
            new VoiceModel { VoiceId = "TxGEqnHWrfWFTfGW9XjX", Name = "Josh", Category = "conversational", Gender = "Male", Description = "Deep and confident male voice for video narration" },
            new VoiceModel { VoiceId = "VR6AewLTigWG4xSOukaG", Name = "Arnold", Category = "narration", Gender = "Male", Description = "Crisp and authoritative male voice" },
            new VoiceModel { VoiceId = "pNInz6obpgDQGcFmaJgB", Name = "Adam", Category = "conversational", Gender = "Male", Description = "Deep and smooth male voice" }
        };
    }

    private void ApplyFilterAndPagination()
    {
        string query = TxtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        int genderIndex = ComboGender?.SelectedIndex ?? 0;

        _filteredVoices = _allVoices.Where(v =>
        {
            bool matchesQuery = string.IsNullOrEmpty(query) ||
                                 v.Name.ToLowerInvariant().Contains(query) ||
                                 v.VoiceId.ToLowerInvariant().Contains(query) ||
                                 v.Description.ToLowerInvariant().Contains(query);

            bool matchesGender = genderIndex == 0 ||
                                 (genderIndex == 1 && v.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase)) ||
                                 (genderIndex == 2 && v.Gender.Equals("Male", StringComparison.OrdinalIgnoreCase));

            return matchesQuery && matchesGender;
        }).ToList();

        int totalItems = _filteredVoices.Count;
        int totalPages = (int)Math.Ceiling((double)totalItems / _pageSize);
        if (totalPages < 1) totalPages = 1;
        if (_currentPage > totalPages) _currentPage = totalPages;

        var pageItems = _filteredVoices.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
        if (LstVoices != null) LstVoices.ItemsSource = pageItems;

        if (TxtPageIndex != null) TxtPageIndex.Text = $"Trang {_currentPage} / {totalPages}";
        if (BtnPrevPage != null) BtnPrevPage.IsEnabled = _currentPage > 1;
        if (BtnNextPage != null) BtnNextPage.IsEnabled = _currentPage < totalPages;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentPage = 1;
        ApplyFilterAndPagination();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        _currentPage = 1;
        ApplyFilterAndPagination();
    }

    private void ComboPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboPageSize.SelectedItem is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int size))
        {
            _pageSize = size;
            _currentPage = 1;
            ApplyFilterAndPagination();
        }
    }

    private void BtnPrevPage_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            ApplyFilterAndPagination();
        }
    }

    private void BtnNextPage_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _currentPage++;
        ApplyFilterAndPagination();
    }

    private void LstVoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstVoices.SelectedItem is VoiceModel voice)
        {
            SelectedVoiceId = voice.VoiceId;
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
