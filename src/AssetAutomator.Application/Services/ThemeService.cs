using System;
using System.Linq;
using System.Windows;

namespace AssetAutomator.Application.Services;

public static class ThemeService
    {
        public static void Apply(bool isDarkMode)
        {
            var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var currentTheme = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Resources/Styles/Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (currentTheme is not null) dictionaries.Remove(currentTheme);
        dictionaries.Add(new ResourceDictionary { Source = new Uri(isDarkMode ? "Resources/Styles/Themes/Dark.xaml" : "Resources/Styles/Themes/Light.xaml", UriKind.Relative) });
    }
}