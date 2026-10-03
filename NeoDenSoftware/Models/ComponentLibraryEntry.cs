namespace NeoDenSoftware.Models;

/// <summary>One row in the Component Library window - a general parts-reference entry (name,
/// description, physical size, an optional photo, an optional datasheet PDF), independent of
/// <see cref="FootprintDefinition"/>/<see cref="Footprints.FootprintLibrary"/> - this is a plain
/// reference library, not wired into BOM footprint matching or board rendering.</summary>
/// <param name="Draw">Free text: where this part is physically stored (e.g. "Drawer 3", "Bin A1") -
/// not a drawing/shape, despite the name.</param>
public sealed record ComponentLibraryEntry(
    string Name,
    string Description,
    string Draw,
    double LengthMm,
    double WidthMm,
    double HeightMm,
    string? ImagePath,
    string? DatasheetPath);
