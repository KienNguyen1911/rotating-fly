using System;
using System.IO;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts a file path string to a BitmapImage (ImageSource) for use with Image.Source in WinUI 3.
/// WinUI 3 x:Bind does NOT automatically coerce string -> ImageSource the way WPF does.
/// Returns null if path is empty, null, or file does not exist.
/// </summary>
public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (File.Exists(path))
                {
                    return new BitmapImage(new Uri(path));
                }
            }
            catch
            {
                // Return null for invalid paths
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
