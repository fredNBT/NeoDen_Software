using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ComponentLibrary;

/// <summary>
/// Persists the Component Library window's entries (name/description/draw/size, an optional
/// photo, an optional datasheet PDF) to disk under %LocalAppData%\NeoDenSoftware, so they survive
/// app restarts. Same plain-JSON whole-file shape, test-isolation env var, cross-process
/// <see cref="Mutex"/> and backup-before-overwrite as <see cref="Footprints.CustomFootprintStore"/> -
/// that class's own history (a real lost-update from two processes racing on the same manifest
/// file, fixed only after real data loss) is the reason this one is built with those protections
/// from the start rather than added after the fact.
/// </summary>
public static class ComponentLibraryStore
{
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string ImagesFolder = Path.Combine(RootFolder, "ComponentImages");
    private static readonly string ManifestPath = Path.Combine(RootFolder, "component_library.json");

    /// <summary>Shown at the bottom of the Component Library window, so the user always knows
    /// exactly where this data lives on disk (worth surfacing plainly, not just documented in a
    /// code comment, given this app's own history with silently-relocated/lost library data).</summary>
    public static string ManifestFilePath => ManifestPath;

    private static readonly Mutex ManifestLock = new(initiallyOwned: false, name: "NeoDenSoftware_ComponentLibraryManifestLock");

    private static List<ManifestEntry> ReadManifest() =>
        File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? []
            : [];

    private static void WriteManifest(List<ManifestEntry> entries)
    {
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

    // Draw defaults to "" (not omitted) so a manifest row written before this field existed
    // deserializes cleanly with no migration step.
    private sealed record ManifestEntry(string Name, string Description, double LengthMm, double WidthMm, double HeightMm,
        string? ImageFileName, string? DatasheetFileName, string Draw = "");

    public static IReadOnlyList<ComponentLibraryEntry> LoadAll()
    {
        if (!File.Exists(ManifestPath)) return [];
        try
        {
            var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(ManifestPath)) ?? [];
            return entries
                .Select(e => new ComponentLibraryEntry(e.Name, e.Description, e.Draw, e.LengthMm, e.WidthMm, e.HeightMm,
                    e.ImageFileName is null ? null : Path.Combine(ImagesFolder, e.ImageFileName),
                    // The datasheet PDF lives right next to the manifest itself, named after the
                    // component - per explicit instruction, not in its own subfolder like images.
                    e.DatasheetFileName is null ? null : Path.Combine(RootFolder, e.DatasheetFileName)))
                .ToList();
        }
        catch (Exception)
        {
            // A corrupt/unreadable manifest shouldn't prevent the app from starting - the library
            // is just unavailable this session, same fallback as CustomFootprintStore.
            return [];
        }
    }

    /// <summary>Adds a new entry, copying the image/datasheet (whichever are given) into permanent
    /// storage first. The image is named after the sanitized component name inside
    /// <c>ComponentImages\</c> (a numeric suffix on collision, same convention as
    /// <see cref="Footprints.CustomFootprintStore"/>); the datasheet PDF is named after the
    /// sanitized component name too, but sits directly beside <c>component_library.json</c> -
    /// per explicit instruction, so the datasheet for "ESP32-S3" is literally
    /// "ESP32-S3.pdf" next to the manifest, not buried in a subfolder.</summary>
    public static ComponentLibraryEntry AddEntry(string name, string description, string draw, double lengthMm, double widthMm, double heightMm,
        string? sourceImagePath, string? sourceDatasheetPath)
    {
        Directory.CreateDirectory(RootFolder);

        ManifestLock.WaitOne();
        try
        {
            string? imageFileName = null;
            if (sourceImagePath is not null)
            {
                Directory.CreateDirectory(ImagesFolder);
                imageFileName = ResolveUniqueFileName(ImagesFolder, name, ".png");
                SaveCroppedImage(sourceImagePath, Path.Combine(ImagesFolder, imageFileName));
            }

            string? datasheetFileName = null;
            if (sourceDatasheetPath is not null)
            {
                datasheetFileName = ResolveUniqueFileName(RootFolder, name, Path.GetExtension(sourceDatasheetPath));
                File.Copy(sourceDatasheetPath, Path.Combine(RootFolder, datasheetFileName), overwrite: true);
            }

            var entries = ReadManifest();
            entries.Add(new ManifestEntry(name, description, lengthMm, widthMm, heightMm, imageFileName, datasheetFileName, draw));
            WriteManifest(entries);

            return new ComponentLibraryEntry(name, description, draw, lengthMm, widthMm, heightMm,
                imageFileName is null ? null : Path.Combine(ImagesFolder, imageFileName),
                datasheetFileName is null ? null : Path.Combine(RootFolder, datasheetFileName));
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Updates an existing entry in place, identified by its current (pre-edit) name.
    /// A null <paramref name="newSourceImagePath"/>/<paramref name="newSourceDatasheetPath"/>
    /// keeps that entry's existing file untouched (only the text fields change); a non-null one
    /// replaces it and deletes the old file, so edits don't leave orphaned files behind - same
    /// rules as <see cref="Footprints.CustomFootprintStore.UpdateFootprint"/>. Renaming a
    /// component does NOT rename its existing datasheet/image file to match, same "a rename alone
    /// never touches an existing file" rule <c>UpdateFootprint</c> already follows for images.</summary>
    public static ComponentLibraryEntry UpdateEntry(string originalName, string newName, string description, string draw,
        double lengthMm, double widthMm, double heightMm, string? newSourceImagePath, string? newSourceDatasheetPath)
    {
        ManifestLock.WaitOne();
        try
        {
            var entries = ReadManifest();
            var index = entries.FindIndex(e => string.Equals(e.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"No component named '{originalName}' found to update.");

            var imageFileName = entries[index].ImageFileName;
            if (newSourceImagePath is not null)
            {
                Directory.CreateDirectory(ImagesFolder);
                var oldImagePath = imageFileName is null ? null : Path.Combine(ImagesFolder, imageFileName);
                imageFileName = ResolveUniqueFileName(ImagesFolder, newName, ".png");
                SaveCroppedImage(newSourceImagePath, Path.Combine(ImagesFolder, imageFileName));
                if (oldImagePath is not null && File.Exists(oldImagePath)) File.Delete(oldImagePath);
            }

            var datasheetFileName = entries[index].DatasheetFileName;
            if (newSourceDatasheetPath is not null)
            {
                var oldDatasheetPath = datasheetFileName is null ? null : Path.Combine(RootFolder, datasheetFileName);
                datasheetFileName = ResolveUniqueFileName(RootFolder, newName, Path.GetExtension(newSourceDatasheetPath));
                File.Copy(newSourceDatasheetPath, Path.Combine(RootFolder, datasheetFileName), overwrite: true);
                if (oldDatasheetPath is not null && File.Exists(oldDatasheetPath)) File.Delete(oldDatasheetPath);
            }

            entries[index] = new ManifestEntry(newName, description, lengthMm, widthMm, heightMm, imageFileName, datasheetFileName, draw);
            WriteManifest(entries);

            return new ComponentLibraryEntry(newName, description, draw, lengthMm, widthMm, heightMm,
                imageFileName is null ? null : Path.Combine(ImagesFolder, imageFileName),
                datasheetFileName is null ? null : Path.Combine(RootFolder, datasheetFileName));
        }
        finally
        {
            ManifestLock.ReleaseMutex();
        }
    }

    /// <summary>Loads the source photo, trims its background margin via
    /// <see cref="ImageCropper.CropToContent"/> so the part fills the whole frame - see that
    /// class's own doc comment for why an uncropped photo renders far too small once stretched
    /// into a footprint's real physical envelope - and saves the result as PNG. Always PNG
    /// regardless of the source format, since the source is decoded and re-encoded here rather
    /// than byte-copied (a plain copy can't crop).</summary>
    private static void SaveCroppedImage(string sourcePath, string destinationPath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
        bitmap.EndInit();

        var cropped = ImageCropper.CropToContent(bitmap);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
    }

    private static string ResolveUniqueFileName(string folder, string name, string extension)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var stem = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrEmpty(stem)) stem = "component";

        var candidate = $"{stem}{extension}";
        var suffix = 2;
        while (File.Exists(Path.Combine(folder, candidate)))
            candidate = $"{stem}_{suffix++}{extension}";

        return candidate;
    }
}
