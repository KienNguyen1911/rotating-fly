using System;
using System.IO;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts a file path string (with optional cache-buster query
/// string, e.g. <c>"C:\foo.png?v=42"</c>) to a BitmapImage. WinUI 3
/// <c>x:Bind</c> does NOT automatically coerce <c>string</c> →
/// <c>ImageSource</c> the way WPF does.
///
/// Cache-busting contract:
///   Bind to <see cref="AssetAutomator.Core.Models.BatchImageItem.ImageCacheKey"/>
///   (not <c>ImagePath</c>). That property returns
///   <c>"path?v={ImageCacheVersion}"</c>; whenever the on-disk file is
///   rewritten in-place (watermark removal, etc.) the provider bumps
///   the version, the URI changes, and this converter rebuilds the
///   BitmapImage — WinUI keys decoded pixels on URI identity so the
///   new pixels show up on the next UI tick.
///
/// Returns null when the path is empty, the file is missing, or the
/// bitmap fails to load — never throws.
/// </summary>
public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        // Strip the cache-buster query so we can probe the filesystem.
        // Uri parsing handles "?" correctly without needing string ops.
        string physicalPath;
        int qIdx = key.IndexOf('?');
        physicalPath = qIdx >= 0 ? key.Substring(0, qIdx) : key;

        try
        {
            if (!File.Exists(physicalPath))
            {
                return null;
            }
            return new BitmapImage(new Uri(key, UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
