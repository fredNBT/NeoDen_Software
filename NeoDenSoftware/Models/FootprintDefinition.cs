namespace NeoDenSoftware.Models;

/// <summary>
/// <paramref name="Name"/> is the canonical/primary name (used for display everywhere - feeder
/// labels, BOM designator info, dropdown text). <paramref name="Aliases"/> are additional names
/// that should also match this same footprint (e.g. a part might be called "0805" in one BOM and
/// "805" in another) - purely additive, never shown in place of Name.
/// <paramref name="ImagePath"/> is an absolute path to a persisted image file (see
/// CustomFootprintStore) - set only for user-added footprints, null for the built-in library,
/// which draws a procedural placeholder instead.
/// </summary>
public sealed record FootprintDefinition(
    string Name,
    double LengthMm,
    double WidthMm,
    double HeightMm,
    FootprintShapeKind ShapeKind,
    int PinCount,
    string? ImagePath = null,
    IReadOnlyList<string>? Aliases = null)
{
    public bool IsCustom => ImagePath is not null;

    public IReadOnlyList<string> Aliases { get; } = Aliases ?? [];

    /// <summary>Name followed by every alias, comma-joined - what the Footprint Library window's
    /// list and edit form show/accept, since the user thinks of this as one set of names rather
    /// than a distinguished primary plus a separate alias list.</summary>
    public string DisplayNames => string.Join(", ", new[] { Name }.Concat(Aliases));
}
