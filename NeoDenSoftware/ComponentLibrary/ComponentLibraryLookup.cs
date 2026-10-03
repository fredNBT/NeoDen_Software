using System.Collections.ObjectModel;
using System.IO;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ComponentLibrary;

/// <summary>
/// Live, process-wide list of every Component Library entry (same shape as
/// <see cref="Footprints.FootprintLibrary.All"/> - loaded once, mutated in place by the
/// Component Library window so an edit there is immediately visible everywhere else) plus the
/// lookup/fallback logic used when rendering a placed component's visual or generating a tray
/// STL - matching a component's BOM Value against an entry's Name, case-insensitive.
/// </summary>
public static class ComponentLibraryLookup
{
    public static ObservableCollection<ComponentLibraryEntry> All { get; } = new(ComponentLibraryStore.LoadAll());

    public static ComponentLibraryEntry? FindByValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : All.FirstOrDefault(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Board/BOM-thumbnail display: prefer the Component Library's own uploaded photo for
    /// this Value over whatever the matched footprint would otherwise draw - including over a
    /// footprint that failed to match at all ("(No Match)"), since the whole point of a photo is
    /// identifying the real part even when the generic footprint library has nothing for it. A
    /// "(No Match)" footprint has no real L/W/H of its own (it's the 0x0 sentinel), so in that one
    /// case the library entry's own dimensions are borrowed too - otherwise the photo would render
    /// at zero size. A real, matched footprint keeps its own L/W/H untouched (physical accuracy for
    /// on-board sizing/hit-testing), only its image is swapped. Falls back to the footprint
    /// completely unchanged when there's no library match, no photo on it, or that photo's file has
    /// gone missing - a broken library reference can never blank out a footprint that already had a
    /// perfectly good image (or, for "(No Match)", the cross marker) of its own.</summary>
    public static FootprintDefinition ResolveDisplayFootprint(FootprintDefinition footprint, string? value)
    {
        var match = FindByValue(value);
        if (match?.ImagePath is not { } libraryImage || !File.Exists(libraryImage)) return footprint;

        return footprint.Name == FootprintLibrary.NoMatch.Name
            ? footprint with { Name = match.Name, ImagePath = libraryImage, LengthMm = match.LengthMm, WidthMm = match.WidthMm, HeightMm = match.HeightMm }
            : footprint with { ImagePath = libraryImage };
    }

    /// <summary>Tray STL export: prefer the Component Library's own measured L/W/H for this Value
    /// over the matched footprint's, so the pocket is sized from the part actually being placed
    /// rather than the generic footprint family. Falls back to the footprint's own dimensions -
    /// unchanged - when there's no library match.</summary>
    public static FootprintDefinition ResolveEffectiveFootprint(FootprintDefinition footprint, string? value)
    {
        var match = FindByValue(value);
        return match is null ? footprint : footprint with { LengthMm = match.LengthMm, WidthMm = match.WidthMm, HeightMm = match.HeightMm };
    }
}
