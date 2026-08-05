using System;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Returns the inverse of a boolean value. Used for IsEnabled bindings.
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }
}
