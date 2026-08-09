using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Converts a boolean to a <see cref="SolidColorBrush"/>. Used by the
/// 9-zone watermark position picker so the currently selected zone is
/// highlighted with a stronger color than the unselected zones.
/// </summary>
/// <remarks>
/// Two-way parameter support:
///   - <c>parameter="true|selected"</c>  → brush returned when value is true
///   - <c>parameter="false|unselected"</c> → brush returned when value is false
/// Defaults to a green Selected / dark-gray Unselected pair when no
/// parameter is supplied.
/// </remarks>
public class BoolToBrushConverter : IValueConverter
{
    /// <summary>Default fill when value = true (e.g. selected zone).</summary>
    public static readonly SolidColorBrush SelectedBrush = new(Color.FromArgb(0xFF, 0x10, 0x7C, 0x10));
    /// <summary>Default fill when value = false (e.g. unselected zone).</summary>
    public static readonly SolidColorBrush UnselectedBrush = new(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isSelected = value is bool b && b;
        return isSelected ? SelectedBrush : UnselectedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
