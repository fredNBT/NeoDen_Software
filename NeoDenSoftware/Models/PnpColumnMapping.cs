namespace NeoDenSoftware.Models;

/// <summary>
/// <paramref name="InvertY"/> negates every Y value on parse - some EDA tools (notably KiCad's
/// "Footprint Position File" export) report Y in a coordinate system whose sign is inverted
/// relative to that same tool's own Gerber output, which otherwise places every component in the
/// wrong spot (usually far off one edge of the board) once combined with the Gerber-derived board
/// offset. Defaults false so this is a pure opt-in that doesn't change any existing (e.g. Altium)
/// import's behavior - see <see cref="Import.PnpImportService.ReadHeader"/> for the heuristic that
/// suggests a starting value for it.
/// </summary>
public sealed record PnpColumnMapping(
    string? DesignatorColumn,
    string? XColumn,
    string? YColumn,
    string? RotationColumn,
    string? SideColumn,
    bool InvertY = false);
