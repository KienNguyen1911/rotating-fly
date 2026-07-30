using System;
using System.Windows.Data;
using System.Globalization;

namespace AssetAutomator.UI.Converters
{
    public class YoutubeUrlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url)
            {
                if (url.StartsWith("https://www.youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase))
                {
                    return url.Substring("https://www.youtube.com/watch?v=".Length);
                }
                if (url.StartsWith("http://www.youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase))
                {
                    return url.Substring("http://www.youtube.com/watch?v=".Length);
                }
                if (url.StartsWith("www.youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase))
                {
                    return url.Substring("www.youtube.com/watch?v=".Length);
                }
            }
            return value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string displayValue)
            {
                string trimmed = displayValue.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
                if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("youtube.com") && !trimmed.Contains("youtu.be"))
                {
                    return "https://www.youtube.com/watch?v=" + trimmed;
                }
                return trimmed;
            }
            return value ?? string.Empty;
        }
    }
}
