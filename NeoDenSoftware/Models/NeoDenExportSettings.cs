namespace NeoDenSoftware.Models;

/// <summary>The NeoDen4 CSV values the user can change (Machine Settings tab). Pick delay and place
/// delay are written on both tape and tray feeder rows; the place height base only on tape rows
/// (place height = base + footprint height).</summary>
public sealed record NeoDenExportSettings(int PickDelayMs = 200, int PlaceDelayMs = 100, double TapePlaceHeightBaseMm = 3.2);
