namespace NeoDenSoftware.Models;

/// <summary>
/// <paramref name="Name"/> is the canonical/primary name (used for display everywhere - feeder
/// labels, BOM designator info, dropdown text). <paramref name="Aliases"/> are additional names
/// that should also match this same footprint (e.g. a part might be called "0805" in one BOM and
/// "805" in another) - purely additive, never shown in place of Name.
/// <paramref name="ImagePath"/> is an absolute path to a persisted image file (see
/// CustomFootprintStore) - set for a user-added footprint that has an uploaded image or a baked
/// STL render, null for the built-in library (and for a user-added footprint the user chose to
/// leave without an image/STL) - both draw the same procedural placeholder outline, from
/// <paramref name="ShapeKind"/>/L/W/H, that the built-in library uses. When a footprint was
/// created from an uploaded STL model, <paramref name="ImagePath"/> is the *rendered* top-down
/// image (baked once at upload time - see Rendering/StlRenderer) and
/// <paramref name="StlSourcePath"/> is the original .stl file, archived purely for
/// provenance/future re-render and never used for live rendering.
/// <paramref name="IsCustom"/> is the actual "user-added" flag (Footprint Library window edit
/// gate, "Custom" grid column) - kept separate from <paramref name="ImagePath"/> being set/null,
/// since a user-added footprint can legitimately have no image (procedural, like a built-in).
/// </summary>
public sealed record FootprintDefinition(
    string Name,
    double LengthMm,
    double WidthMm,
    double HeightMm,
    FootprintShapeKind ShapeKind,
    int PinCount,
    string? ImagePath = null,
    IReadOnlyList<string>? Aliases = null,
    string? StlSourcePath = null,
    bool IsCustom = false)
{
    /// <summary>What the Footprint Library window's "Source" column shows for this row.</summary>
    public string SourceLabel => StlSourcePath is not null ? "STL" : ImagePath is not null ? "Image" : IsCustom ? "Shape" : "Built-in";

    public IReadOnlyList<string> Aliases { get; } = Aliases ?? [];

    /// <summary>Name followed by every alias, comma-joined - what the Footprint Library window's
    /// list and edit form show/accept, since the user thinks of this as one set of names rather
    /// than a distinguished primary plus a separate alias list.</summary>
    public string DisplayNames => string.Join(", ", new[] { Name }.Concat(Aliases));
}
