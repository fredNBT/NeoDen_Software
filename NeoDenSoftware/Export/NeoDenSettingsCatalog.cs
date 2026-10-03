namespace NeoDenSoftware.Export;

/// <summary>One field of one CSV row type. <see cref="IsComputed"/> is true when the value comes
/// from the project (BOM, Settings tab, fiducials...) rather than being a fixed number.</summary>
public sealed record MachineSettingRow(string Field, string Column, string Value, string Source, bool IsComputed);

public sealed record MachineSettingsSection(string Title, string Description, IReadOnlyList<MachineSettingRow> Rows);

/// <summary>
/// Describes, row type by row type and field by field (same order as the exported NeoDen4 CSV),
/// every value "Create NeoDen4 File..." writes and where it comes from. Fixed values are read from
/// <see cref="NeoDenMachineDefaults"/> - the same constants the exporter uses.
/// </summary>
public static class NeoDenSettingsCatalog
{
    private const string TemplateSource = "Hard-coded. Copied from your real NeoDenTemplate CSV.";
    private const string ExampleRowSource = "Hard-coded. Copied from your real example row (feeder 1) - you asked for every other value to stay as in that example.";
    private const string TrayPatternSource = "Hard-coded. Taken from the tray-feeder row pattern you supplied.";
    private const string SpecSource = "Hard-coded rule from your written spec for the feeder table.";

    public static IReadOnlyList<MachineSettingsSection> Build() =>
    [
        BuildFiles(),
        BuildTapeStack(),
        BuildTrayStack(),
        BuildMark(),
        .. NeoDenMachineDefaults.AfterMarkBoilerplateLines.Select(BuildBoilerplate),
        BuildMirrorCreate(),
        BuildMirror(),
        BuildComp(),
    ];

    private static MachineSettingRow Fixed(string field, string column, string value, string source) =>
        new(field, column, value.Length == 0 ? "(empty)" : value, source, false);

    private static MachineSettingRow Computed(string field, string column, string value, string source) =>
        new(field, column, value, source, true);

    private static string Name(string[] header, int field) =>
        field <= header.Length && header[field - 1].Length > 0 ? header[field - 1] : "(no header)";

    private static MachineSettingRow Padding(int firstField) =>
        Fixed($"{firstField}-{NeoDenMachineDefaults.RowWidth}", "(no header)", "", "Empty padding - every row is written 33 fields wide, like your real template.");

    private static MachineSettingsSection BuildFiles() => new(
        "Files written",
        "Create NeoDen4 File... asks for a base file name and writes these next to it.",
        [
            Computed("-", "Top file", "<name>_Top.csv", "One NeoDen4 CSV per board side; contains only the Top-side feeders and components."),
            Computed("-", "Bottom file", "<name>_Bottom.csv", "Same layout, Bottom side only."),
            Computed("-", "Board outline", "<name>_Outline.dxf", "Written only when an Outline layer was imported."),
            Computed("-", "Tray STLs", "<name>_Tray_<Value>_<Footprint>.stl", "One per distinct part with Tray Feeder ticked. Tray size, pocket clearance and label settings are in Settings > Tray STL Generator Settings."),
            Fixed("-", "Not written", "pcb row", "The real template also has a \"pcb\" row before \"mark\". It has never been asked for, so it is not exported."),
        ]);

    private static MachineSettingsSection BuildTapeStack()
    {
        var header = NeoDenMachineDefaults.StackHeader.Split(',');
        var rows = new List<MachineSettingRow>
        {
            Fixed("1", Name(header, 1), "stack", "Row-type keyword. One stack row is written per distinct tape feeder used on that side."),
            Computed("2", Name(header, 2), "(feeder number)", "The feeder number from Auto-Assign Feeders (or set by hand): 1-49 for normal parts. Parts sharing Value + Footprint share one feeder."),
            Fixed("3", Name(header, 3), $"{NeoDenMachineDefaults.TapeType(0)} if Feeder ID < {NeoDenMachineDefaults.TapeTypeSplitFeederId}, else {NeoDenMachineDefaults.TapeType(NeoDenMachineDefaults.TapeTypeSplitFeederId)}", SpecSource),
            Fixed("4", Name(header, 4), NeoDenMachineDefaults.Nozzle, "Hard-coded: always 1 for now."),
            Computed("5", Name(header, 5), "(feeder slot X)", "Looked up from Settings > Tape Feeder Positions by feeder number (your supplied defaults plus any edits you saved). It is the feeder's fixed position, not a component's."),
            Computed("6", Name(header, 6), "(feeder slot Y)", "Same lookup as X."),
            Fixed("7", Name(header, 7), $"{NeoDenMachineDefaults.TapeAngle(0)} if Feeder ID < {NeoDenMachineDefaults.TapeAngleSplitFeederId}, else {NeoDenMachineDefaults.TapeAngle(NeoDenMachineDefaults.TapeAngleSplitFeederId)}", SpecSource),
            Computed("8", Name(header, 8), "(footprint name)", "The footprint selected for the part in the BOM panel."),
            Computed("9", Name(header, 9), "(BOM value)", "The Value column of the BOM."),
            Fixed("10", Name(header, 10), NeoDenMachineDefaults.TapePickHeight.ToString("0.#"), SpecSource),
            Computed("11", Name(header, 11), "(Pick delay above)", $"Set by you in the Editable values box above (default {NeoDenMachineDefaults.DefaultPickDelayMs}). Same value on tape and tray rows."),
            Computed("12", Name(header, 12), "(base above) + footprint height", $"The Place height base you set in the Editable values box above (default {NeoDenMachineDefaults.DefaultTapePlaceHeightBase}) plus the Height (mm) of the selected footprint in the Footprint Library."),
            Computed("13", Name(header, 13), "(Place delay above)", $"Set by you in the Editable values box above (default {NeoDenMachineDefaults.DefaultPlaceDelayMs}). Same value on tape and tray rows."),
        };
        for (var i = 0; i < NeoDenMachineDefaults.TapeTail.Length; i++)
        {
            var field = 14 + i;
            rows.Add(Fixed(field.ToString(), Name(header, field), NeoDenMachineDefaults.TapeTail[i], ExampleRowSource));
        }
        return new("stack - tape feeder row (Feeder ID under 50)", NeoDenMachineDefaults.StackHeader.TrimEnd(','), rows);
    }

    private static MachineSettingsSection BuildTrayStack()
    {
        var header = NeoDenMachineDefaults.StackHeader.Split(',');
        var rows = new List<MachineSettingRow>
        {
            Fixed("1", Name(header, 1), "stack", "Row-type keyword. One stack row is written per distinct tray feeder used on that side."),
            Computed("2", Name(header, 2), "(feeder number)", "The feeder number from Auto-Assign Feeders for parts with Tray Feeder ticked: 54-99."),
            Fixed("3", Name(header, 3), NeoDenMachineDefaults.TrayType, TrayPatternSource),
            Fixed("4", Name(header, 4), NeoDenMachineDefaults.Nozzle, "Hard-coded: always 1 for now."),
            Computed("5", Name(header, 5), "(tray Begin X)", "Looked up from Settings > Tray Feeder Positions by feeder number."),
            Computed("6", Name(header, 6), "(tray Begin Y)", "Same lookup as X."),
            Fixed("7", Name(header, 7), NeoDenMachineDefaults.TrayAngle, TrayPatternSource),
            Computed("8", Name(header, 8), "(footprint name)", "The footprint selected for the part in the BOM panel."),
            Computed("9", Name(header, 9), "(BOM value)", "The Value column of the BOM."),
            Fixed("10", Name(header, 10), NeoDenMachineDefaults.TrayPickHeight.ToString("0.#"), "Hard-coded. Your correction - it was 1, you changed it to 1.7."),
            Computed("11", Name(header, 11), "(Pick delay above)", $"Set by you in the Editable values box above (default {NeoDenMachineDefaults.DefaultPickDelayMs}). Same value on tape and tray rows."),
            Computed("12", Name(header, 12), $"footprint height + {NeoDenMachineDefaults.TrayPlaceHeightBase}", $"The Height (mm) of the selected footprint plus a hard-coded {NeoDenMachineDefaults.TrayPlaceHeightBase} (from your tray pattern)."),
            Computed("13", Name(header, 13), "(Place delay above)", $"Set by you in the Editable values box above (default {NeoDenMachineDefaults.DefaultPlaceDelayMs}). Same value on tape and tray rows."),
        };
        for (var i = 0; i < NeoDenMachineDefaults.TrayHead.Length; i++)
        {
            var field = 14 + i;
            rows.Add(Fixed(field.ToString(), Name(header, field), NeoDenMachineDefaults.TrayHead[i], TrayPatternSource));
        }
        rows.Add(Computed("18", "(no header)", "(Columns)", "The tray's Columns from Settings > Tray Feeder Positions - the pattern you supplied showed these four fields hold Columns, Rows, End X and End Y."));
        rows.Add(Computed("19", "(no header)", "(Rows)", "The tray's Rows from the same table."));
        rows.Add(Computed("20", "(no header)", "(End X)", "The tray's End X from the same table."));
        rows.Add(Computed("21", "(no header)", "(End Y)", "The tray's End Y from the same table."));
        for (var i = 0; i < NeoDenMachineDefaults.TrayTail.Length; i++)
            rows.Add(Fixed((22 + i).ToString(), "(no header)", NeoDenMachineDefaults.TrayTail[i], TrayPatternSource));
        return new("stack - tray feeder row (Feeder ID 50 and above)", "Same header as the tape row.", rows);
    }

    private static MachineSettingsSection BuildMark()
    {
        var rows = new List<MachineSettingRow>();
        for (var i = 0; i < NeoDenMachineDefaults.MarkHead.Length; i++)
            rows.Add(Fixed((i + 1).ToString(), "(no header)", NeoDenMachineDefaults.MarkHead[i], TemplateSource));
        rows.Add(Computed("4", "(no header)", "(X of fiducial 1)", "First fiducial listed for this side in the Layers sidebar. A typed value has the board offset added; 0 if none is defined."));
        rows.Add(Computed("5", "(no header)", "(Y of fiducial 1)", "Same fiducial."));
        rows.Add(Computed("6", "(no header)", "(X of fiducial 2)", "Second fiducial for this side; 0 if fewer than two are defined."));
        rows.Add(Computed("7", "(no header)", "(Y of fiducial 2)", "Same fiducial."));
        rows.Add(Padding(8));
        return new("mark", "No header row. The four numbers used to be the example board's own coordinates; you asked for them to come from the board's fiducials instead.", rows);
    }

    private static MachineSettingsSection BuildBoilerplate(string line)
    {
        var fields = line.Split(',');
        var used = fields.Length;
        while (used > 0 && fields[used - 1].Length == 0) used--;
        var rows = new List<MachineSettingRow>();
        for (var i = 0; i < used; i++)
            rows.Add(Fixed((i + 1).ToString(), "(no header)", fields[i], TemplateSource));
        rows.Add(Padding(used + 1));
        var title = fields[0] == "markext" ? $"markext (mark {fields[1]})" : fields[0];
        return new(title, "No header row. Written as-is after the mark row.", rows);
    }

    private static MachineSettingsSection BuildMirrorCreate()
    {
        var rows = new List<MachineSettingRow>();
        var head = NeoDenMachineDefaults.MirrorCreateHead;
        for (var i = 0; i < head.Length; i++)
            rows.Add(Fixed((i + 1).ToString(), "(no header)", head[i], TemplateSource));
        rows.Add(Computed("4", "(no header)", "(X of first component)", "X of the first placed component on this side, in BOM order - Bottom X is mirrored when \"Invert X on bottom layers\" is ticked."));
        rows.Add(Computed("5", "(no header)", "(Y of first component)", "Y of the same component. Both are 0 if nothing is placed on this side."));
        var tail = NeoDenMachineDefaults.MirrorCreateTail;
        for (var i = 0; i < tail.Length; i++)
            rows.Add(Fixed((6 + i).ToString(), "(no header)", tail[i], TemplateSource));
        rows.Add(Padding(6 + tail.Length));
        return new("mirror_create", "No header row.", rows);
    }

    private static MachineSettingsSection BuildMirror()
    {
        var rows = new List<MachineSettingRow>
        {
            Fixed("1", "(no header)", "mirror", TemplateSource),
            Computed("2", "(no header)", "(X of first component)", "Same value as mirror_create."),
            Computed("3", "(no header)", "(Y of first component)", "Same value as mirror_create."),
        };
        var tail = NeoDenMachineDefaults.MirrorTail;
        for (var i = 0; i < tail.Length; i++)
            rows.Add(Fixed((4 + i).ToString(), "(no header)", tail[i], TemplateSource));
        rows.Add(Padding(4 + tail.Length));
        return new("mirror", "No header row.", rows);
    }

    private static MachineSettingsSection BuildComp()
    {
        var header = NeoDenMachineDefaults.SmdHeader.Split(',');
        var rows = new List<MachineSettingRow>
        {
            Fixed("1", Name(header, 1), "comp", "Row-type keyword. One comp row is written per placed component (not per feeder), in BOM order."),
            Computed("2", Name(header, 2), "(feeder number)", "The component's assigned feeder; blank if none is assigned yet."),
            Fixed("3", Name(header, 3), NeoDenMachineDefaults.CompNozzle, "Hard-coded: always 1. The real template varies this, but you overrode it."),
            Computed("4", Name(header, 4), "(designator)", "The BOM designator."),
            Computed("5", Name(header, 5), "(BOM value)", "The Value column of the BOM."),
            Fixed("6", Name(header, 6), "", "Left blank on purpose - every comp row in your real template leaves it blank."),
            Computed("7", Name(header, 7), "(X)", "PnP X plus the board offset. Bottom X is mirrored when \"Invert X on bottom layers\" is ticked."),
            Computed("8", Name(header, 8), "(Y)", "PnP Y plus the board offset."),
            Computed("9", Name(header, 9), "(rotation)", "PnP rotation, including any rotation you added with the spacebar in Board View."),
            Fixed("10", Name(header, 10), NeoDenMachineDefaults.CompSkip, "Hard-coded: always No, per your instruction."),
            Padding(11),
        };
        return new("comp", NeoDenMachineDefaults.SmdHeader.TrimEnd(','), rows);
    }
}
