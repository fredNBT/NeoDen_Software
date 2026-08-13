namespace NeoDenSoftware.Models;

/// <summary>One saved <see cref="MainViewModel"/>.Components row - just enough to re-apply a
/// user's edits (footprint override, rotation, feeder assignment) on top of a fresh re-import from
/// the original BOM/PnP files. A designator present in the BOM but missing from this list means
/// the user had removed that component before saving.</summary>
public sealed record ProjectComponentRow(string Designator, string FootprintName, double RotationDegrees, int? FeederNumber, bool UseHighFeederBank);

/// <summary>One saved fiducial row (Top or Bottom) - the full list replaces whatever auto-import
/// would detect, since fiducials can be manually added/edited/removed in ways a fresh BOM/PnP
/// re-import wouldn't reproduce on its own.</summary>
public sealed record ProjectFiducialRow(BoardSide Side, string Designator, double X, double Y);

/// <summary>One saved per-file Gerber layer-role assignment, keyed by filename (matched against a
/// fresh zip extraction's own filenames on load) - captures whatever the user confirmed in the
/// layer-assignment dialog, which may differ from what auto-detection alone would guess (e.g. an
/// outline file auto-detected as something else and manually corrected).</summary>
public sealed record ProjectLayerRoleRow(string FileName, GerberLayerRole Role);

/// <summary>Everything needed to restore a session: the original source file paths (so the board
/// can be re-imported without asking the user to re-pick files), confirmed column mappings and
/// per-file layer-role assignments (so neither the column-mapping nor the layer-confirmation
/// dialog needs to be re-answered), plus every editable piece of state that isn't purely derived
/// from those source files.</summary>
public sealed record ProjectData(
    string? GerberZipPath,
    string? BomPath,
    string? PnpPath,
    double BoardOriginX,
    double BoardOriginY,
    double PcbThicknessMm,
    bool MirrorBottomLayers,
    BomColumnMapping? BomMapping,
    PnpColumnMapping? PnpMapping,
    IReadOnlyList<ProjectComponentRow> Components,
    IReadOnlyList<ProjectFiducialRow> Fiducials,
    IReadOnlyList<ProjectLayerRoleRow> LayerRoles);
