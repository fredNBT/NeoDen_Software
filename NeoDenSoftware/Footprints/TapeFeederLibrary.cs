using NeoDenSoftware.Models;

namespace NeoDenSoftware.Footprints;

/// <summary>
/// Default tape feeder slot positions/rotations for this machine (world-space mm, same
/// coordinate frame as the other fixtures - not board-relative). These are starting values;
/// the user can adjust them later from the Settings tab.
/// </summary>
public static class TapeFeederLibrary
{
    public static IReadOnlyList<(string Number, TapeFeederXY Position)> Defaults { get; } =
    [
        ("1", new TapeFeederXY(407.71, 87.38, 90)),
        ("2", new TapeFeederXY(411.43, 101.15, 90)),
        ("3", new TapeFeederXY(411.73, 115.27, 90)),
        ("4", new TapeFeederXY(411.44, 128.68, 90)),
        ("5", new TapeFeederXY(411.58, 142.41, 90)),
        ("6", new TapeFeederXY(411.46, 155.85, 90)),
        ("7", new TapeFeederXY(411.63, 169.53, 90)),
        ("8", new TapeFeederXY(0.4, 22, 90)),
        ("9", new TapeFeederXY(0.4, 22, 90)),
        ("10", new TapeFeederXY(0.4, 22, 90)),
        ("11", new TapeFeederXY(0.4, 22, 90)),
        ("12", new TapeFeederXY(409.71, 302.11, 90)),
        ("13", new TapeFeederXY(409.14, 315.73, 90)),
        ("21", new TapeFeederXY(24.68, 21.73, -90)),
        ("22", new TapeFeederXY(24.6, 35.46, -90)),
        ("23", new TapeFeederXY(24.71, 48.86, -90)),
        ("24", new TapeFeederXY(24.65, 62.62, -90)),
        ("25", new TapeFeederXY(24.45, 76.23, -90)),
        ("26", new TapeFeederXY(24.71, 89.64, -90)),
        ("27", new TapeFeederXY(29, 104.59, -90)),
        ("28", new TapeFeederXY(25.10, 118.07, -90)),
        ("29", new TapeFeederXY(24.31, 131.72, -90)),
        ("30", new TapeFeederXY(24.25, 145.01, -90)),
        ("31", new TapeFeederXY(23.13, 158.73, -90)),
        ("32", new TapeFeederXY(25.54, 172.72, -90)),
        ("33", new TapeFeederXY(24.98, 186.48, -90)),
        ("34", new TapeFeederXY(24.93, 213.40, -90)),
        ("35", new TapeFeederXY(25.33, 213.40, -90)),
        ("36", new TapeFeederXY(24.90, 226.90, -90)),
        ("37", new TapeFeederXY(25.10, 240.87, -90)),
        ("38", new TapeFeederXY(25.44, 254.56, -90)),
        ("39", new TapeFeederXY(24.86, 268.07, -90)),
        ("40", new TapeFeederXY(25.93, 280.71, -90)),
        ("41", new TapeFeederXY(26.01, 294.95, -90)),
    ];
}
