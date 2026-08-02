using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetAutomator.WinUI.ViewModels;

/// <summary>
/// Shared view-model for the slide-in sidebar drawer that lives in <c>MainWindow</c>.
/// Pages push their live logs into this drawer via <see cref="Show"/> / <see cref="AppendLog"/>;
/// the drawer is rendered by the overlay Grid in MainWindow.xaml (D2).
/// </summary>
public partial class SidebarViewModel : ObservableObject
{
    public const double MinWidth = 320;
    public const double MaxWidthRatio = 0.8;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "Task Live Logs";

    [ObservableProperty]
    private string _logs = string.Empty;

    [ObservableProperty]
    private double _drawerWidth = 520;

    /// <summary>
    /// Replaces the current title, clears the log buffer, and opens the drawer.
    /// </summary>
    public void Show(string title, string? initialLogs = null)
    {
        Title = title;
        Logs = initialLogs ?? string.Empty;
        IsOpen = true;
    }

    /// <summary>
    /// Appends a single line to the log buffer. No-op when the drawer is closed.
    /// </summary>
    public void AppendLog(string line)
    {
        if (!IsOpen) return;
        Logs += line + Environment.NewLine;
    }

    /// <summary>
    /// Appends many lines at once (e.g. when a task is selected).
    /// </summary>
    public void AppendLogs(IEnumerable<string> lines)
    {
        if (!IsOpen) return;
        var sb = new StringBuilder(Logs);
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
        Logs = sb.ToString();
    }

    public void Clear()
    {
        Logs = string.Empty;
    }

    [RelayCommand]
    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
    }

    partial void OnIsOpenChanged(bool value)
    {
        // No-op for now; future hook point for telemetry / persistence.
    }
}
