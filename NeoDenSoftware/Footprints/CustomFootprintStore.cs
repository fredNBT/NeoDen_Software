using System.IO;
using System.Text.Json;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Footprints;

/// <summary>
/// Persists user-added footprints (dimensions + an uploaded image) to disk under
/// %LocalAppData%\NeoDenSoftware, so they survive app restarts and rebuilds - unlike the
/// built-in library, which is hardcoded in source and resets to nothing extra on every build.
/// The uploaded image itself is copied into permanent storage rather than referenced by its
/// original path, since that original file could move, be renamed, or be deleted after import.
/// Designed to scale to many thousands of entries prepared outside the app (hand-edited or
/// scripted): the manifest is plain JSON, and images are named after the footprint's primary
/// name (not an opaque id) so the folder stays browsable/auditable at that scale.
/// </summary>
public static class CustomFootprintStore
{
    // Overridable so test harnesses can point this at an isolated scratch directory instead of
    // the user's real permanent library - a prior version of this class had no such seam, and a
    // test process reading/writing the same file the live app was using at the same time caused
    // a real lost-update (one process's write silently clobbered a row the other had just added).
    // Must be set (if at all) before anything in this process ever touches FootprintLibrary, since
    // these are static-initialized once per process.
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string ImagesFolder = Path.Combine(RootFolder, "FootprintImages");
    private static readonly string ManifestPath = Path.Combine(RootFolder, "custom_footprints.json");

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    // Aliases defaults to null (not []) so old manifest rows written before this field existed -
    // e.g. a real entry added via the very first version of this window - deserialize cleanly
    // with no migration step needed.
    private sealed record ManifestEntry(string Name, double LengthMm, double WidthMm, double HeightMm, string ImageFileName, List<string>? Aliases = null);

    public static IReadOnlyList<FootprintDefinition> LoadAll()
    {
        if (!File.Exists(ManifestPath)) return [];

        try
        {
            var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? [];
            return entries
                .Select(e => new FootprintDefinition(e.Name, e.LengthMm, e.WidthMm, e.HeightMm,
                    FootprintShapeKind.Custom, 0, Path.Combine(ImagesFolder, e.ImageFileName), e.Aliases))
                .ToList();
        }
        catch (Exception)
        {
            // A corrupt/unreadable manifest shouldn't prevent the app from starting - it just
            // means custom footprints are unavailable this session (the built-in library still
            // works fine on its own).
            return [];
        }
    }

    /// <summary>Copies <paramref name="sourceImagePath"/> into permanent storage (named after the
    /// sanitized primary name, with a numeric suffix on collision) and appends a new entry to the
    /// manifest, returning the resulting definition (with the persisted image path, not the
    /// original one the user picked).</summary>
    public static FootprintDefinition AddFootprint(string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string sourceImagePath)
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(ImagesFolder);

        var imageFileName = ResolveUniqueImageFileName(name, Path.GetExtension(sourceImagePath));
        var persistedImagePath = Path.Combine(ImagesFolder, imageFileName);
        File.Copy(sourceImagePath, persistedImagePath, overwrite: true);

        var entries = File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? []
            : new List<ManifestEntry>();
        entries.Add(new ManifestEntry(name, lengthMm, widthMm, heightMm, imageFileName, aliases.ToList()));
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(entries));

        return new FootprintDefinition(name, lengthMm, widthMm, heightMm, FootprintShapeKind.Custom, 0, persistedImagePath, aliases);
    }

    /// <summary>Updates an existing custom footprint's manifest entry in place, identified by its
    /// current (pre-edit) primary name. If <paramref name="newSourceImagePath"/> is null, the
    /// existing image file is kept untouched (only the name/aliases/dimensions change) - renaming
    /// a footprint alone never renames or moves its image file, so anything tracking the images
    /// folder externally by filename isn't disrupted by an in-app rename. A replacement image is
    /// named after the new primary name and the old image file is deleted, so edits don't leave
    /// orphaned files behind.</summary>
    public static FootprintDefinition UpdateFootprint(string originalName, string newName, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string? newSourceImagePath)
    {
        var entries = File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? []
            : new List<ManifestEntry>();

        var index = entries.FindIndex(e => string.Equals(e.Name, originalName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException($"No custom footprint named '{originalName}' found to update.");

        var imageFileName = entries[index].ImageFileName;
        if (newSourceImagePath is not null)
        {
            Directory.CreateDirectory(ImagesFolder);
            var oldImagePath = Path.Combine(ImagesFolder, imageFileName);

            imageFileName = ResolveUniqueImageFileName(newName, Path.GetExtension(newSourceImagePath));
            File.Copy(newSourceImagePath, Path.Combine(ImagesFolder, imageFileName), overwrite: true);

            if (File.Exists(oldImagePath)) File.Delete(oldImagePath);
        }

        entries[index] = new ManifestEntry(newName, lengthMm, widthMm, heightMm, imageFileName, aliases.ToList());
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(entries));

        return new FootprintDefinition(newName, lengthMm, widthMm, heightMm, FootprintShapeKind.Custom, 0, Path.Combine(ImagesFolder, imageFileName), aliases);
    }

    /// <summary>Turns a footprint name into a filesystem-safe filename stem, resolving a
    /// collision (another image already using that stem) by appending "_2", "_3", etc. - keeps
    /// the images folder human-browsable by name at thousands-of-parts scale instead of opaque
    /// GUIDs, while still guaranteeing every file gets a unique name.</summary>
    private static string ResolveUniqueImageFileName(string name, string extension)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var stem = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrEmpty(stem)) stem = "footprint";

        var candidate = $"{stem}{extension}";
        var suffix = 2;
        while (File.Exists(Path.Combine(ImagesFolder, candidate)))
            candidate = $"{stem}_{suffix++}{extension}";

        return candidate;
    }
}
