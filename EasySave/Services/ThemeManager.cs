using System;
using System.Windows;

namespace EasySave.Services;

public enum AppTheme { Light, Dark }

public static class ThemeManager
{
    private const string LightUri = "Resources/Theme.Light.xaml";
    private const string DarkUri  = "Resources/Theme.Dark.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        string uri = theme == AppTheme.Dark ? DarkUri : LightUri;
        var dict = new ResourceDictionary
        {
            Source = new Uri(uri, UriKind.Relative)
        };

        var merged = app.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString;
            if (src is not null && (src.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase)
                                 || src.EndsWith("Theme.Dark.xaml",  StringComparison.OrdinalIgnoreCase)))
            {
                merged.RemoveAt(i);
            }
        }
        merged.Add(dict);
        Current = theme;
    }

    public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
