using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using AssetAutomator.Core.Models;

namespace AssetAutomator.WinUI.Converters;

public class NodeStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value switch
        {
            NodeStatus nodeStatus => nodeStatus.ToString(),
            string text => text,
            _ => string.Empty
        };

        return new SolidColorBrush(status.ToLowerInvariant() switch
        {
            "running" or "generating..." or "processing" => ParseHexColor("#FFC107"),
            "success" or "done" or "completed" => ParseHexColor("#4CAF50"),
            "failed" or "error" => ParseHexColor("#F44336"),
            "idle" or "pending" or "waiting" => ParseHexColor("#475569"),
            _ => ParseHexColor("#607D8B")
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    public static Color ParseHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Microsoft.UI.Colors.Gray;
        hex = hex.TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch { }
        return Microsoft.UI.Colors.Gray;
    }
}

public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            return new SolidColorBrush(NodeStatusToBrushConverter.ParseHexColor(hex));
        }
        return new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
