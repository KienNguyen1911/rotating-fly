using System;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Calculates card height based on Aspect Ratio string ("16:9", "9:16", "1:1", "4:3", "3:4").
/// Supports binding value as aspect ratio string with parameter as width (double/string),
/// or value as width with parameter as aspect ratio string.
/// </summary>
public class AspectRatioHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double width = 320.0;
        string aspectRatio = "16:9";

        if (value is double dWidth && dWidth > 0)
        {
            width = dWidth;
            if (parameter is string pRatio && !string.IsNullOrWhiteSpace(pRatio))
            {
                aspectRatio = pRatio;
            }
        }
        else if (value is string sRatio && !string.IsNullOrWhiteSpace(sRatio))
        {
            aspectRatio = sRatio;
            if (parameter != null && double.TryParse(parameter.ToString(), out double pWidth) && pWidth > 0)
            {
                width = pWidth;
            }
        }

        double ratio = aspectRatio switch
        {
            "9:16" => 16.0 / 9.0,
            "1:1" => 1.0,
            "4:3" => 3.0 / 4.0,
            "3:4" => 4.0 / 3.0,
            _ => 9.0 / 16.0 // "16:9" default
        };

        double calculatedHeight = width * ratio;
        return Math.Max(120.0, calculatedHeight);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
