using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using AssetAutomator.Core.Models;

namespace AssetAutomator.WinUI.Converters
{
    /// <summary>
    /// Maps a <see cref="BatchImageItem.WatermarkRemoved"/> boolean to a brush:
    ///   - <c>true</c>      → green (✨ Clean indicator)
    ///   - <c>false</c>     → light blue (💧 watermarked indicator)
    ///   - null            → gray (── no badge)
    /// </summary>
    public class WatermarkStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool? removed = value as bool?;
            string? note = parameter as string;

            if (removed == true)
            {
                return new SolidColorBrush(ParseHexColor("#1F107C06")); // green tint
            }
            if (removed == false)
            {
                return new SolidColorBrush(ParseHexColor("#1F2563EB")); // blue tint
            }
            return new SolidColorBrush(ParseHexColor("#1F797979")); // gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();

        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Microsoft.UI.Colors.Gray;
            hex = hex.TrimStart('#');
            try
            {
                if (hex.Length == 8)
                {
                    byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch { }
            return Microsoft.UI.Colors.Gray;
        }
    }

    /// <summary>
    /// Maps a <see cref="BatchImageItem.WatermarkRemoved"/> boolean to a
    /// foreground (text) brush, contrasted against the background tint:
    ///   - <c>true</c>      → bright green
    ///   - <c>false</c>     → light blue
    ///   - null            → mid gray
    /// </summary>
    public class WatermarkStatusToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool? removed = value as bool?;
            return (removed == true)
                ? new SolidColorBrush(ParseHexColor("#FF107C10"))
                : (removed == false)
                    ? new SolidColorBrush(ParseHexColor("#FF2563EB"))
                    : new SolidColorBrush(ParseHexColor("#FF797979"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();

        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Microsoft.UI.Colors.Gray;
            hex = hex.TrimStart('#');
            try
            {
                if (hex.Length == 8)
                {
                    byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch { }
            return Microsoft.UI.Colors.Gray;
        }
    }
}
