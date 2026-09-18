using System.Windows;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Appearance;

/// <summary>
/// Applies a color theme app-wide by merging/removing a <see cref="ResourceDictionary"/> into
/// <see cref="Application.Resources"/> - every window picks up the change immediately (including
/// ones already open) since WPF re-resolves resource lookups live, no restart needed. Only one
/// theme dictionary is ever merged at a time; <see cref="AppTheme.Original"/> means none at all,
/// restoring plain default Windows styling.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _currentThemeDictionary;

    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        if (_currentThemeDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_currentThemeDictionary);
            _currentThemeDictionary = null;
        }

        var fileName = theme switch
        {
            AppTheme.FabFloor => "FabFloor.xaml",
            AppTheme.BenchLight => "BenchLight.xaml",
            AppTheme.CopperFlux => "CopperFlux.xaml",
            _ => null,
        };
        if (fileName is null) return; // Original - no dictionary to merge

        // A plain relative Uri resolves against Application.ResourceAssembly, which defaults to
        // the ENTRY assembly - correct for the real app (NeoDenSoftware.exe is its own entry
        // assembly) but silently wrong for anything that loads this assembly as a library instead
        // (e.g. a test harness), throwing "Cannot locate resource". Naming the assembly explicitly
        // in the pack URI makes this resolve correctly regardless of which assembly is the entry
        // point.
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/NeoDenSoftware;component/Themes/{fileName}", UriKind.Absolute),
        };
        app.Resources.MergedDictionaries.Add(dictionary);
        _currentThemeDictionary = dictionary;
    }
}
