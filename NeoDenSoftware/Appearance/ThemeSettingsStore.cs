using System.IO;
using System.Text.Json;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Appearance;

/// <summary>
/// Persists the selected <see cref="AppTheme"/> to disk under %LocalAppData%\NeoDenSoftware, so it
/// survives app restarts. Mirrors the other stores' (<see cref="Stl.TrayStlSettingsStore"/>,
/// <see cref="Feeders.FeederPositionStore"/>) plain-JSON, read-modify-write-whole-file approach and
/// shared test-isolation env var.
/// </summary>
public static class ThemeSettingsStore
{
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string FilePath = Path.Combine(RootFolder, "theme.json");

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    private sealed record StoreData(AppTheme Theme);

    /// <summary>Falls back to <see cref="AppTheme.Original"/> on first run or a corrupt/unreadable
    /// file - a bad settings file shouldn't prevent the app from starting, it just means the
    /// default Windows look is used this session.</summary>
    public static AppTheme Load()
    {
        if (!File.Exists(FilePath)) return AppTheme.Original;
        try
        {
            var data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(FilePath));
            return data?.Theme ?? AppTheme.Original;
        }
        catch (Exception)
        {
            return AppTheme.Original;
        }
    }

    public static void Save(AppTheme theme)
    {
        Directory.CreateDirectory(RootFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new StoreData(theme)));
    }
}
