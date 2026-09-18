using System.IO;
using System.Text.Json;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Stl;

/// <summary>
/// Persists <see cref="TrayStlSettings"/> to disk under %LocalAppData%\NeoDenSoftware, so edits
/// made in the "Tray STL Generator Settings..." window survive app restarts. Mirrors
/// <see cref="Feeders.FeederPositionStore"/>'s plain-JSON, read-modify-write-whole-file approach
/// and its test-isolation seam (same env var - every store under this app's data root must share
/// it, so a test process never touches the real file while the live app is also writing to it).
/// </summary>
public static class TrayStlSettingsStore
{
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string FilePath = Path.Combine(RootFolder, "tray_stl_settings.json");

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    /// <summary>Falls back to <see cref="TrayStlSettings"/>'s own defaults on first run or a
    /// corrupt/unreadable file - a bad settings file shouldn't prevent the app from starting or
    /// exporting, it just means the tray generator uses its hardcoded defaults this session.</summary>
    public static TrayStlSettings Load()
    {
        if (!File.Exists(FilePath)) return new TrayStlSettings();
        try
        {
            return JsonSerializer.Deserialize<TrayStlSettings>(File.ReadAllText(FilePath)) ?? new TrayStlSettings();
        }
        catch (Exception)
        {
            return new TrayStlSettings();
        }
    }

    public static void Save(TrayStlSettings settings)
    {
        Directory.CreateDirectory(RootFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
