using System;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts a bool to a double:
///   true  → spacing value from ConverterParameter (default 12)
///   false → 0 so no gap is left between collapsed columns
/// Use on properties like Grid.ColumnSpacing where 0 means "no gap".
/// </summary>
public class BoolToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            if (parameter != null && double.TryParse(parameter.ToString(), out double d))
            {
                return d;
            }
            return 12.0;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}