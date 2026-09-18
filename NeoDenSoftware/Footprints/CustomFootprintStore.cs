using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;
using NeoDenSoftware.Stl;

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
    private static readonly string StlSourcesFolder = Path.Combine(RootFolder, "FootprintStlSources");
    private static readonly string ManifestPath = Path.Combine(RootFolder, "custom_footprints.json");

    // Cross-process lock guarding every read-modify-write of the manifest below (every Add*/
    // Update* method acquires it for its whole body). The comment above documents the historical
    // failure this guards against: two processes touching the same manifest file at once, each
    // reading it before the other's write landed, so the later write silently discarded whatever
    // the first had just added - not a one-time fluke, it can recur any time two copies of this
    // app (or a test harness pointed at the real store by mistake) run at the same time. A named
    // Mutex serializes every call across every process on the machine, not just within one, so the
    // second writer always sees the first writer's result before making its own change.
    private static readonly Mutex ManifestLock = new(initiallyOwned: false, name: "NeoDenSoftware_FootprintManifestLock");

    private static List<ManifestEntry> ReadManifest() =>
        File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? []
            : [];

    private static void WriteManifest(List<ManifestEntry> entries)
    {
        // A one-generation backup, refreshed on every write - if a bad write ever lands here again
        // (whatever the cause), the previous known-good state is one file rename away from being
        // restored by hand instead of being gone for good.
        if (File.Exists(ManifestPath))
            File.Copy(ManifestPath, ManifestPath + ".bak", overwrite: true);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(entries));
    }

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    // Aliases/StlFileName/ShapeKind default to null (not []/omitted) so old manifest rows written
    // before those fields existed - e.g. a real entry added via the very first version of this
    // window - deserialize cleanly with no migration step needed. ImageFileName is nullable to
    // support a footprint the user deliberately left without an image/STL (see
    // AddFootprintProcedural) - it renders as a procedural placeholder shape, same as a built-in.
    private sealed record ManifestEntry(string Name, double LengthMm, double WidthMm, double HeightMm, string? ImageFileName, List<string>? Aliases = null, string? StlFileName = null, FootprintShapeKind? ShapeKind = null);

    public static IReadOnlyList<FootprintDefinition> LoadAll()
    {
        if (!File.Exists(ManifestPath)) return [];

        try
        {
            var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? [];
            return entries
                .Select(e => new FootprintDefinition(e.Name, e.LengthMm, e.WidthMm, e.HeightMm,
                    e.ShapeKind ?? FootprintShapeKind.Custom, 0,
                    e.ImageFileName is null ? null : Path.Combine(ImagesFolder, e.ImageFileName), e.Aliases,
                    e.StlFileName is null ? null : Path.Combine(StlSourcesFolder, e.StlFileName), IsCustom: true))
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

        ManifestLock.WaitOne();
        try
        {
            var imageFileName = ResolveUniqueFileName(ImagesFolder, name, Path.GetExtension(sourceImagePath));
            var persistedImagePath = Path.Combine(ImagesFolder, imageFileName);
            File.Copy(sourceImagePath, persistedImagePath, overwrite: true);

            var entries = ReadManifest();
            entries.Add(new ManifestEntry(name, lengthMm, widthMm, heightMm, imageFileName, aliases.ToList()));
            WriteManifest(entries);

            return new FootprintDefinition(name, lengthMm, widthMm, heightMm, FootprintShapeKind.Custom, 0, persistedImagePath, aliases, IsCustom: true);
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Adds a new user-defined footprint with NO image or STL - it renders as a
    /// procedural placeholder outline (body rectangle + end caps/pin-1 marker, from
    /// <paramref name="shapeKind"/> and L/W/H), exactly the same way the built-in library's
    /// entries do (see <see cref="Rendering.FootprintRenderer"/>). No image/STL files are created;
    /// only a manifest entry.</summary>
    public static FootprintDefinition AddFootprintProcedural(string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, FootprintShapeKind shapeKind)
    {
        Directory.CreateDirectory(RootFolder);

        ManifestLock.WaitOne();
        try
        {
            var entries = ReadManifest();
            entries.Add(new ManifestEntry(name, lengthMm, widthMm, heightMm, null, aliases.ToList(), null, shapeKind));
            WriteManifest(entries);

            return new FootprintDefinition(name, lengthMm, widthMm, heightMm, shapeKind, 0, null, aliases, null, IsCustom: true);
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Same as <see cref="AddFootprint"/>, but the "image" is rendered once from an
    /// uploaded STL model (top-down, via <see cref="StlRenderer"/>) instead of being a photo the
    /// user picked directly. The rendered PNG is stored exactly like any other footprint image;
    /// the original .stl is additionally archived (for provenance/future re-render only, never
    /// read back for live rendering).</summary>
    public static FootprintDefinition AddFootprintFromStl(string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string sourceStlPath)
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(StlSourcesFolder);

        var mesh = StlParser.Parse(sourceStlPath);
        var rendered = StlRenderer.RenderTopDown(mesh);

        ManifestLock.WaitOne();
        try
        {
            var imageFileName = ResolveUniqueFileName(ImagesFolder, name, ".png");
            SavePng(rendered, Path.Combine(ImagesFolder, imageFileName));

            var stlFileName = ResolveUniqueFileName(StlSourcesFolder, name, StlExtensionOf(sourceStlPath));
            File.Copy(sourceStlPath, Path.Combine(StlSourcesFolder, stlFileName), overwrite: true);

            var entries = ReadManifest();
            entries.Add(new ManifestEntry(name, lengthMm, widthMm, heightMm, imageFileName, aliases.ToList(), stlFileName));
            WriteManifest(entries);

            return new FootprintDefinition(name, lengthMm, widthMm, heightMm, FootprintShapeKind.Custom, 0,
                Path.Combine(ImagesFolder, imageFileName), aliases, Path.Combine(StlSourcesFolder, stlFileName), IsCustom: true);
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
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
        ManifestLock.WaitOne();
        try
        {
            var entries = ReadManifest();

            var index = entries.FindIndex(e => string.Equals(e.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"No custom footprint named '{originalName}' found to update.");

            var imageFileName = entries[index].ImageFileName;
            // A brand-new plain image supersedes any STL this footprint used to be rendered from -
            // drop the manifest's reference to it (the archived .stl itself is left on disk rather
            // than deleted, since it's still a legitimate record of what was originally uploaded).
            // Editing name/dimensions alone (newSourceImagePath is null) leaves this untouched, same
            // as the image file itself staying untouched below. imageFileName may already be null here
            // (the entry being edited was procedural, with no image) - handled the same way as "no old
            // file to clean up" rather than as an error.
            var stlFileName = newSourceImagePath is null ? entries[index].StlFileName : null;
            if (newSourceImagePath is not null)
            {
                Directory.CreateDirectory(ImagesFolder);
                var oldImagePath = imageFileName is null ? null : Path.Combine(ImagesFolder, imageFileName);

                imageFileName = ResolveUniqueFileName(ImagesFolder, newName, Path.GetExtension(newSourceImagePath));
                File.Copy(newSourceImagePath, Path.Combine(ImagesFolder, imageFileName), overwrite: true);

                if (oldImagePath is not null && File.Exists(oldImagePath)) File.Delete(oldImagePath);
            }

            entries[index] = new ManifestEntry(newName, lengthMm, widthMm, heightMm, imageFileName, aliases.ToList(), stlFileName);
            WriteManifest(entries);

            return new FootprintDefinition(newName, lengthMm, widthMm, heightMm, FootprintShapeKind.Custom, 0,
                imageFileName is null ? null : Path.Combine(ImagesFolder, imageFileName), aliases,
                stlFileName is null ? null : Path.Combine(StlSourcesFolder, stlFileName), IsCustom: true);
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Same as <see cref="UpdateFootprint"/>, but for a footprint that has (or will have)
    /// no image/STL - re-persists the name/dimensions/shape as a procedural placeholder, cleaning
    /// up any image/STL files the entry previously had (e.g. switching a footprint that used to
    /// have an uploaded image back to a plain shape), so edits don't leave orphaned files behind.</summary>
    public static FootprintDefinition UpdateFootprintProcedural(string originalName, string newName, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, FootprintShapeKind shapeKind)
    {
        ManifestLock.WaitOne();
        try
        {
            var entries = ReadManifest();

            var index = entries.FindIndex(e => string.Equals(e.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"No custom footprint named '{originalName}' found to update.");

            var oldImagePath = entries[index].ImageFileName is { } oldImageFile ? Path.Combine(ImagesFolder, oldImageFile) : null;
            var oldStlPath = entries[index].StlFileName is { } oldStlFile ? Path.Combine(StlSourcesFolder, oldStlFile) : null;
            if (oldImagePath is not null && File.Exists(oldImagePath)) File.Delete(oldImagePath);
            if (oldStlPath is not null && File.Exists(oldStlPath)) File.Delete(oldStlPath);

            entries[index] = new ManifestEntry(newName, lengthMm, widthMm, heightMm, null, aliases.ToList(), null, shapeKind);
            WriteManifest(entries);

            return new FootprintDefinition(newName, lengthMm, widthMm, heightMm, shapeKind, 0, null, aliases, null, IsCustom: true);
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Same as <see cref="UpdateFootprint"/>, but replacing this footprint's rendered
    /// image with a fresh render from a NEW uploaded STL - both the old rendered PNG and the old
    /// archived .stl are deleted, so edits don't leave orphaned files behind (mirroring
    /// <see cref="UpdateFootprint"/>'s own image-replacement cleanup).</summary>
    public static FootprintDefinition UpdateFootprintFromStl(string originalName, string newName, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string newSourceStlPath)
    {
        ManifestLock.WaitOne();
        try
        {
            var entries = ReadManifest();

            var index = entries.FindIndex(e => string.Equals(e.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"No custom footprint named '{originalName}' found to update.");

            Directory.CreateDirectory(ImagesFolder);
            Directory.CreateDirectory(StlSourcesFolder);

            var oldImagePath = entries[index].ImageFileName is { } oldImageFile ? Path.Combine(ImagesFolder, oldImageFile) : null;
            var oldStlPath = entries[index].StlFileName is { } oldStlFile ? Path.Combine(StlSourcesFolder, oldStlFile) : null;

            var mesh = StlParser.Parse(newSourceStlPath);
            var rendered = StlRenderer.RenderTopDown(mesh);

            var imageFileName = ResolveUniqueFileName(ImagesFolder, newName, ".png");
            SavePng(rendered, Path.Combine(ImagesFolder, imageFileName));
            if (oldImagePath is not null && File.Exists(oldImagePath)) File.Delete(oldImagePath);

            var stlFileName = ResolveUniqueFileName(StlSourcesFolder, newName, StlExtensionOf(newSourceStlPath));
            File.Copy(newSourceStlPath, Path.Combine(StlSourcesFolder, stlFileName), overwrite: true);
            if (oldStlPath is not null && File.Exists(oldStlPath)) File.Delete(oldStlPath);

            entries[index] = new ManifestEntry(newName, lengthMm, widthMm, heightMm, imageFileName, aliases.ToList(), stlFileName);
            WriteManifest(entries);

            return new FootprintDefinition(newName, lengthMm, widthMm, heightMm, FootprintShapeKind.Custom, 0,
                Path.Combine(ImagesFolder, imageFileName), aliases, Path.Combine(StlSourcesFolder, stlFileName), IsCustom: true);
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Turns a footprint name into a filesystem-safe filename stem, resolving a
    /// collision (another file in <paramref name="folder"/> already using that stem) by appending
    /// "_2", "_3", etc. - keeps the folder human-browsable by name at thousands-of-parts scale
    /// instead of opaque GUIDs, while still guaranteeing every file gets a unique name. Shared by
    /// both the rendered-image folder and the archived-STL folder.</summary>
    private static string ResolveUniqueFileName(string folder, string name, string extension)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var stem = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrEmpty(stem)) stem = "footprint";

        var candidate = $"{stem}{extension}";
        var suffix = 2;
        while (File.Exists(Path.Combine(folder, candidate)))
            candidate = $"{stem}_{suffix++}{extension}";

        return candidate;
    }

    private static string StlExtensionOf(string sourceStlPath) =>
        Path.GetExtension(sourceStlPath) is { Length: > 0 } ext ? ext : ".stl";

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
