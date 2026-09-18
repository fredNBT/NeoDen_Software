namespace NeoDenSoftware.Models;

/// <summary>
/// User-editable parameters for <see cref="Stl.TrayStlGenerator"/> - the parts tray STL generated
/// per footprint for tray feeders when "Create NeoDen4 File..." runs. A solid box
/// (fixed overall size - <paramref name="BoxLengthMm"/>/<paramref name="BoxWidthMm"/>/
/// <paramref name="BoxHeightMm"/>, default 135x35x6mm) with <paramref name="PocketCount"/> evenly
/// spaced rectangular pockets recessed into the top, sized per footprint, plus a groove recessed
/// along both long edges - modeled directly from a real reference tray the user provided (a real
/// Fusion 360 design and technical drawing for a SOIC-16 part). Persisted via
/// <see cref="Stl.TrayStlSettingsStore"/>, edited via the Settings tab's
/// "Tray STL Generator Settings..." window.
/// </summary>
public sealed record TrayStlSettings(
    double BoxLengthMm = 135,
    double BoxWidthMm = 35,
    double BoxHeightMm = 6,
    int PocketCount = 5,
    double ExtraLengthMm = 2,
    double ExtraWidthMm = 2,
    double GrooveWidthMm = 2,
    double GrooveDepthMm = 2,
    double TextDepthMm = 1,
    double TextHeightMm = 4);
