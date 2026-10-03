using System.IO;
using System.Text.Json;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Appearance;

/// <summary>
/// Persists per-layer-role color overrides (the Layers panel's "click a swatch to change that
/// layer's color" feature) to %LocalAppData%\NeoDenSoftware, same plain-JSON whole-file approach
/// and test-isolation env var as <see cref="ThemeSettingsStore"/>/<see cref="Stl.TrayStlSettingsStore"/>.
/// </summary>
public static class LayerColorStore
{
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string FilePath = Path.Combine(RootFolder, "layer_colors.json");

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    // ARGB bytes rather than a WPF Color directly - keeps this store free of any WPF/System.Windows
    // dependency, matching how every other model in this app's Stores stays UI-framework-agnostic.
    private sealed record StoredColor(byte A, byte R, byte G, byte B);

    /// <summary>Falls back to an empty map on first run or a corrupt/unreadable file - callers
    /// already have their own hardcoded default color per role, so a missing override for a role
    /// just means "use that default", same as every other settings store in this app.</summary>
    public static IReadOnlyDictionary<GerberLayerRole, (byte A, byte R, byte G, byte B)> LoadAll()
    {
        if (!File.Exists(FilePath)) return new Dictionary<GerberLayerRole, (byte, byte, byte, byte)>();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, StoredColor>>(File.ReadAllText(FilePath)) ?? [];
            var result = new Dictionary<GerberLayerRole, (byte, byte, byte, byte)>();
            foreach (var (key, color) in raw)
                if (Enum.TryParse<GerberLayerRole>(key, out var role))
                    result[role] = (color.A, color.R, color.G, color.B);
            return result;
        }
        catch (Exception)
        {
            return new Dictionary<GerberLayerRole, (byte, byte, byte, byte)>();
        }
    }

    public static void Save(GerberLayerRole role, byte a, byte r, byte g, byte b)
    {
        Directory.CreateDirectory(RootFolder);
        var raw = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<Dictionary<string, StoredColor>>(File.ReadAllText(FilePath)) ?? []
            : new Dictionary<string, StoredColor>();
        raw[role.ToString()] = new StoredColor(a, r, g, b);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(raw));
    }
}
