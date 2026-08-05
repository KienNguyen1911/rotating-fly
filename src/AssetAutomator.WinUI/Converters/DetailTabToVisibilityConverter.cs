using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Maps a DetailTab enum value to Visibility — Visible when the active tab matches
/// ConverterParameter, Collapsed otherwise. Used to swap panel content without
/// re-creating it (preserves scroll position and UI state).
/// </summary>
public class DetailTabToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value != null && parameter != null &&
            string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}