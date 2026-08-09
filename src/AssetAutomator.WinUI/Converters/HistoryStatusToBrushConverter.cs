using System;
using System.Globalization;
using AssetAutomator.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts HistoryTaskStatus → SolidColorBrush (background cho status pill).
/// </summary>
public class HistoryStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not HistoryTaskStatus status)
            return new SolidColorBrush(Colors.Transparent);

        var hex = status switch
        {
            HistoryTaskStatus.Running    => "#1F0078D4",
            HistoryTaskStatus.Success   => "#1F107C06",
            HistoryTaskStatus.Failed   => "#1FC42F1C",
            HistoryTaskStatus.Cancelled => "#1F797979",
            _ => null,
        };

        return hex != null
            ? new SolidColorBrush(ParseHexColor(hex))
            : new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        return Windows.UI.Color.FromArgb(
            byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
    }
}

/// <summary>
/// Converts HistoryTaskStatus → SolidColorBrush (foreground cho text trong status pill).
/// </summary>
public class HistoryStatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not HistoryTaskStatus status)
            return new SolidColorBrush(Colors.Black);

        var hex = status switch
        {
            HistoryTaskStatus.Running    => "#0078D4",
            HistoryTaskStatus.Success   => "#107C06",
            HistoryTaskStatus.Failed   => "#C42F1C",
            HistoryTaskStatus.Cancelled => "#797979",
            _ => null,
        };

        return hex != null
            ? new SolidColorBrush(ParseHexColor(hex))
            : new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        return Windows.UI.Color.FromArgb(
            byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
            byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
    }
}