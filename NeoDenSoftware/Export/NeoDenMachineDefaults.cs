namespace NeoDenSoftware.Export;

/// <summary>
/// Every fixed value the NeoDen4 CSV export writes, in one place - the exporter
/// (MainViewModel) and the "Machine Settings" tab (<see cref="NeoDenSettingsCatalog"/>) both read
/// these, so the tab can never drift from what is actually written to the file.
/// </summary>
public static class NeoDenMachineDefaults
{
    /// <summary>Every row in the file is padded to this many fields.</summary>
    public const int RowWidth = 33;

    public const string StackHeader =
        "#Feeder,Feeder ID,Type,Nozzle,X,Y,Angle,Footprint,Value,Pick height,Pick delay,Place Height,Place Delay," +
        "Vacuum detection,Threshold,Vision Alignment,Speed,,,,,,,,,,,,,,,,";

    public const string SmdHeader =
        "#SMD,Feeder ID,Nozzle,Name,Value,Footprint,X,Y,Rotation,Skip,,,,,,,,,,,,,,,,,,,,,,,";

    public static readonly string[] AfterMarkBoilerplateLines =
    [
        "markext,0,0.8,3,1,0,,,,,,,,,,,,,,,,,,,,,,,,,,,",
        "markext,1,0.8,3,1,0,,,,,,,,,,,,,,,,,,,,,,,,,,,",
        "test,No,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,",
    ];

    public const string Nozzle = "1";

    // ---- stack row, tape feeder (Feeder ID normally 1-40) ----
    public const int TapeTypeSplitFeederId = 50;
    public static int TapeType(int feederId) => feederId < TapeTypeSplitFeederId ? 0 : 1;
    public const int TapeAngleSplitFeederId = 20;
    public static int TapeAngle(int feederId) => feederId < TapeAngleSplitFeederId ? 90 : -90;
    public const double TapePickHeight = 0.5;
    public const int DefaultPickDelayMs = 200;
    public const double DefaultTapePlaceHeightBase = 3.2;
    public const int DefaultPlaceDelayMs = 100;

    /// <summary>Fields 14-33 of a tape row (Vacuum detection onward).</summary>
    public static readonly string[] TapeTail =
        ["No", "-40", "1", "40", "4", "50", "50", "No", "No", "-40", "-40", "-40", "-40", "-1", "-1", "0", "0", "", "", ""];

    // ---- stack row, tray feeder (Feeder ID normally 54-99) ----
    public const string TrayType = "1";
    public const string TrayAngle = "0";
    public const double TrayPickHeight = 1.7;
    public const double TrayPlaceHeightBase = 1.7;

    /// <summary>Fields 14-17 of a tray row (Vacuum detection .. Speed).</summary>
    public static readonly string[] TrayHead = ["Yes", "-40", "1", "40"];

    /// <summary>Fields 22-33 of a tray row (after Columns/Rows/End X/End Y).</summary>
    public static readonly string[] TrayTail = ["1", "1", "No", "No", "-40", "-40", "-40", "-40", "-1", "-1", "0", "0"];

    // ---- mark / mirror rows ----
    public static readonly string[] MarkHead = ["mark", "Whole", "Auto"];
    public static readonly string[] MirrorCreateHead = ["mirror_create", "1", "1"];
    public static readonly string[] MirrorCreateTail = ["0", "0", "339.7", "134.57", "0.170895"];
    public static readonly string[] MirrorTail = ["0", "No"];

    // ---- comp row ----
    public const string CompNozzle = "1";
    public const string CompSkip = "No";
}
