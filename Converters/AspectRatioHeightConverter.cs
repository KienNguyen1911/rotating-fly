using System;
using System.Globalization;
using System.Windows.Data;

namespace AssetAutomator
{
    /// <summary>
    /// Dynamically calculates Height from ActualWidth and AspectRatio string ("16:9", "9:16", "1:1", "4:3", "3:4").
    /// Eliminates the need for Viewbox scaling so font sizes and corner radii remain exact in screen pixels.
    /// </summary>
    public class AspectRatioHeightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[0] is double width && width > 0)
            {
                string aspectRatio = values[1] as string ?? "16:9";
                double ratio = aspectRatio switch
                {
                    "9:16" => 16.0 / 9.0,
                    "1:1" => 1.0,
                    "4:3" => 3.0 / 4.0,
                    "3:4" => 4.0 / 3.0,
                    _ => 9.0 / 16.0 // "16:9" default
                };

                double calculatedHeight = width * ratio;
                return Math.Max(60, calculatedHeight);
            }

            return 200.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
