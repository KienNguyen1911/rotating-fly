using System.Windows;

namespace AutoCreateImage;

public static class ThemeService
{
    public static void Apply(bool isDarkMode)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var currentTheme = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Resources/Styles/Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (currentTheme is not null) dictionaries.Remove(currentTheme);
        dictionaries.Add(new ResourceDictionary { Source = new Uri(isDarkMode ? "Resources/Styles/Themes/Dark.xaml" : "Resources/Styles/Themes/Light.xaml", UriKind.Relative) });
    }
}
