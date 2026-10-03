using System.IO;
using System.Text.Json;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Export;

/// <summary>Persists <see cref="NeoDenExportSettings"/> under %LocalAppData%\NeoDenSoftware, same
/// approach (and test-isolation env var) as <see cref="Stl.TrayStlSettingsStore"/>.</summary>
public static class NeoDenExportSettingsStore
{
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string FilePath = Path.Combine(RootFolder, "neoden_export_settings.json");

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    public static NeoDenExportSettings Load()
    {
        if (!File.Exists(FilePath)) return new NeoDenExportSettings();
        try
        {
            return JsonSerializer.Deserialize<NeoDenExportSettings>(File.ReadAllText(FilePath)) ?? new NeoDenExportSettings();
        }
        catch (Exception)
        {
            return new NeoDenExportSettings();
        }
    }

    public static void Save(NeoDenExportSettings settings)
    {
        Directory.CreateDirectory(RootFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
