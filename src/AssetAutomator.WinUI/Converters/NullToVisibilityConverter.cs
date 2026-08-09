using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Inverse of BoolToVisibilityConverter behavior: returns Visible when the
/// value is <c>null</c>, empty string, or false. Used for placeholder text
/// layered under an Image.Source bound to a BindableImageSource.
///
/// Targets:
///   - BitmapImage (or any object)  → Visible when null, else Collapsed
///   - string                        → Visible when null/empty, else Collapsed
///   - bool                          → Visible when false, else Collapsed
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isEmpty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            bool b => !b,
            _ => false,
        };
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
