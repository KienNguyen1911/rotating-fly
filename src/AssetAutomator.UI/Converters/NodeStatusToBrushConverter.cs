using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AssetAutomator.Core.Models;

namespace AssetAutomator.UI.Converters
{
    public class NodeStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NodeStatus status)
            {
                return status switch
                {
                    NodeStatus.Running => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107")), // Yellow/Gold glow
                    NodeStatus.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")), // Green
                    NodeStatus.Failed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")),  // Red
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#607D8B"))                   // Blue-grey idle
                };
            }

            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                }
                catch { }
            }
            return new SolidColorBrush(Colors.DodgerBlue);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
