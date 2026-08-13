using NeoDenSoftware.Models;

namespace NeoDenSoftware.Footprints;

/// <summary>
/// Default tray feeder slot positions for this machine (world-space mm, same coordinate frame as
/// the other fixtures). These are starting values; the user can adjust them later from the
/// Settings tab, same as <see cref="TapeFeederLibrary"/>.
/// </summary>
public static class TrayFeederLibrary
{
    public static IReadOnlyList<TrayFeederPosition> Defaults { get; } =
    [
        new(54, 93.50, 65.20, 203, 65.20, 5, 1),
        new(55, 93.50, 110, 203, 110, 5, 1),
        new(56, 93.50, 155, 203, 155, 5, 1),
        new(57, 93.50, 200, 203, 200, 5, 1),
    ];
}
