using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts a double (pixel width) to a Microsoft.UI.Xaml.GridLength so we can
/// bind a ColumnDefinition's Width to a ViewModel property in pixel units.
/// </summary>
public class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
        {
            return new GridLength(d);
        }
        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is GridLength gl)
        {
            return gl.Value;
        }
        return 0d;
    }
}