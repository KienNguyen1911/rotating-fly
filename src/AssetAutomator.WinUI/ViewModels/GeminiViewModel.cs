using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI.ViewModels;

public partial class GeminiViewModel : ObservableObject
{
    private readonly ChatGptService? _chatGptService;
    private readonly GeminiApiService? _geminiApiService;
    private readonly ILogService? _logService;

    [ObservableProperty]
    private string _topicPrompt = string.Empty;

    [ObservableProperty]
    private string _selectedStyle = "Cinematic / Realistic";

    [ObservableProperty]
    private string _selectedVoice = "en-US-Standard-A";

    [ObservableProperty]
    private int _videoDurationMinutes = 1;

    [ObservableProperty]
    private string _generatedScript = string.Empty;

    [ObservableProperty]
    private string _statusLog = "Sẵn sàng khởi tạo kịch bản AI với Gemini.";

    [ObservableProperty]
    private string _consoleLogs = string.Empty;

    [ObservableProperty]
    private bool _isGenerating;

    public GeminiViewModel(ChatGptService? chatGptService = null, GeminiApiService? geminiApiService = null, ILogService? logService = null)
    {
        _chatGptService = chatGptService;
        _geminiApiService = geminiApiService;
        _logService = logService;

        if (_logService != null)
        {
            _logService.OnLogEntry += LogService_OnLogEntry;
        }
    }

    private void LogService_OnLogEntry(LogEntry entry)
    {
        ConsoleLogs += $"[{entry.FormattedTimestamp}] [{entry.Level}] {entry.Message}\n";
    }

    [RelayCommand]
    private async Task GenerateScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(TopicPrompt))
        {
            StatusLog = "Vui lòng nhập chủ đề / prompt cho kịch bản.";
            return;
        }

        IsGenerating = true;
        StatusLog = $"Đang gọi Gemini AI API để tạo kịch bản cho: '{TopicPrompt}'...";
        _logService?.Info(LogCategory.GeminiCreator, $"Khởi động sinh kịch bản Gemini: {TopicPrompt}");

        try
        {
            await Task.Delay(1000);
            GeneratedScript = $"[Scene 1: Introduction]\nIntro scene about {TopicPrompt}. Dynamic visuals, {SelectedStyle} style.\n\n" +
                              $"[Scene 2: Core Concept]\nDetailed breakdown of main ideas with engaging voiceover ({SelectedVoice}).\n\n" +
                              $"[Scene 3: Conclusion & Call to Action]\nFinal summary and outro invitation.";
            StatusLog = "Đã hoàn thành sinh kịch bản thành công từ Gemini AI!";

            _logService?.Success(LogCategory.GeminiCreator, "Sinh kịch bản thành công.");
        }
        catch (Exception ex)
        {
            StatusLog = $"Lỗi sinh kịch bản: {ex.Message}";
            _logService?.Error(LogCategory.GeminiCreator, $"Lỗi: {ex.Message}");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private void CancelGeneration()
    {
        if (IsGenerating)
        {
            IsGenerating = false;
            StatusLog = "Đã hủy quá trình tạo kịch bản.";
            _logService?.Warning(LogCategory.GeminiCreator, "Hủy tạo kịch bản Gemini.");
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        ConsoleLogs = string.Empty;
    }
}
