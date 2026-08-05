using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts a bool to GridLength:
///   true  → Star (1*) so the column takes available space
///   false → 0 so the column collapses and sibling column gets all the room
/// Used by master-detail ColumnDefinitions where the detail column must collapse to 0
/// when hidden — relying solely on Border.Visibility still leaves the column width behind.
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return new GridLength(1, GridUnitType.Star);
        }
        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}