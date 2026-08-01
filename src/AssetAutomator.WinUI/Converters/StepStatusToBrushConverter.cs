using System;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Maps a step status string to a Brush (Brush wrapper of StepStatusToBrushConverter).
/// Pending → gray, Running → amber, Done → green, Failed → red.
/// </summary>
public class StepStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = (value as string ?? string.Empty).ToLowerInvariant();
        Color color = status switch
        {
            "running" => Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B), // amber
            "done" => Color.FromArgb(0xFF, 0x10, 0xB9, 0x81),     // green
            "failed" => Color.FromArgb(0xFF, 0xEF, 0x44, 0x44),    // red
            _ => Color.FromArgb(0xFF, 0x47, 0x55, 0x69),          // slate-600
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}