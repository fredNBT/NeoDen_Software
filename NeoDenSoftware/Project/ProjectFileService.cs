using System.Globalization;
using System.IO;
using NeoDenSoftware.Import;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Project;

/// <summary>
/// Reads/writes a whole-session project file (".pnp" extension, per explicit request): plain CSV,
/// one row-type keyword in column 0 per line - the same convention already used by the NeoDen4
/// export ("stack"/"mark"/"comp"/...) - so a project remembers the original Gerber/BOM/PnP source
/// files and confirmed column mappings, plus every edit layered on top of them (footprint changes,
/// rotations, feeder assignments, removed BOM rows, fiducials). Loading re-imports from the
/// original source files (they must still exist at their saved paths) and re-applies the saved
/// edits on top - it does not embed the Gerber geometry or BOM/PnP rows directly, keeping the
/// project file small and human-readable.
/// </summary>
public static class ProjectFileService
{
    public static void Save(string path, ProjectData data)
    {
        var lines = new List<string>();

        void Meta(string key, string value) => lines.Add(Join("meta", key, value));
        Meta("GerberZipPath", data.GerberZipPath ?? "");
        Meta("BomPath", data.BomPath ?? "");
        Meta("PnpPath", data.PnpPath ?? "");
        Meta("BoardOriginX", data.BoardOriginX.ToString(CultureInfo.InvariantCulture));
        Meta("BoardOriginY", data.BoardOriginY.ToString(CultureInfo.InvariantCulture));
        Meta("PcbThicknessMm", data.PcbThicknessMm.ToString(CultureInfo.InvariantCulture));
        Meta("MirrorBottomLayers", data.MirrorBottomLayers.ToString());

        if (data.BomMapping is not null)
        {
            lines.Add(Join("bomcolumn", "Designator", data.BomMapping.DesignatorColumn ?? ""));
            lines.Add(Join("bomcolumn", "Value", data.BomMapping.ValueColumn ?? ""));
            lines.Add(Join("bomcolumn", "Footprint", data.BomMapping.FootprintColumn ?? ""));
        }

        if (data.PnpMapping is not null)
        {
            lines.Add(Join("pnpcolumn", "Designator", data.PnpMapping.DesignatorColumn ?? ""));
            lines.Add(Join("pnpcolumn", "X", data.PnpMapping.XColumn ?? ""));
            lines.Add(Join("pnpcolumn", "Y", data.PnpMapping.YColumn ?? ""));
            lines.Add(Join("pnpcolumn", "Rotation", data.PnpMapping.RotationColumn ?? ""));
            lines.Add(Join("pnpcolumn", "Side", data.PnpMapping.SideColumn ?? ""));
        }

        foreach (var c in data.Components)
        {
            lines.Add(Join("component", c.Designator, c.FootprintName,
                c.RotationDegrees.ToString("0.####", CultureInfo.InvariantCulture),
                c.FeederNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                c.UseHighFeederBank.ToString()));
        }

        foreach (var f in data.Fiducials)
        {
            lines.Add(Join("fiducial", f.Side.ToString(), f.Designator,
                f.X.ToString("0.####", CultureInfo.InvariantCulture),
                f.Y.ToString("0.####", CultureInfo.InvariantCulture)));
        }

        foreach (var r in data.LayerRoles)
            lines.Add(Join("layerrole", r.FileName, r.Role.ToString()));

        File.WriteAllLines(path, lines);
    }

    public static ProjectData Load(string path)
    {
        var rows = File.ReadAllLines(path).Select(CsvParser.ParseLine).ToList();

        string? Meta(string key) =>
            rows.FirstOrDefault(r => r.Length >= 3 && r[0] == "meta" && r[1] == key) is { } r ? r[2] : null;
        string? BomColumn(string field) =>
            NullIfEmpty(rows.FirstOrDefault(r => r.Length >= 3 && r[0] == "bomcolumn" && r[1] == field) is { } r ? r[2] : null);
        string? PnpColumn(string field) =>
            NullIfEmpty(rows.FirstOrDefault(r => r.Length >= 3 && r[0] == "pnpcolumn" && r[1] == field) is { } r ? r[2] : null);

        var components = rows
            .Where(r => r.Length >= 6 && r[0] == "component")
            .Select(r => new ProjectComponentRow(
                r[1],
                r[2],
                ParseDouble(r[3]),
                string.IsNullOrEmpty(r[4]) ? null : int.Parse(r[4], CultureInfo.InvariantCulture),
                bool.TryParse(r[5], out var high) && high))
            .ToList();

        var fiducials = rows
            .Where(r => r.Length >= 5 && r[0] == "fiducial")
            .Select(r => new ProjectFiducialRow(
                Enum.TryParse<BoardSide>(r[1], out var side) ? side : BoardSide.Top,
                r[2], ParseDouble(r[3]), ParseDouble(r[4])))
            .ToList();

        var layerRoles = rows
            .Where(r => r.Length >= 3 && r[0] == "layerrole")
            .Select(r => new ProjectLayerRoleRow(r[1], Enum.TryParse<GerberLayerRole>(r[2], out var role) ? role : GerberLayerRole.Unknown))
            .ToList();

        return new ProjectData(
            NullIfEmpty(Meta("GerberZipPath")),
            NullIfEmpty(Meta("BomPath")),
            NullIfEmpty(Meta("PnpPath")),
            ParseDouble(Meta("BoardOriginX") ?? "243.52"),
            ParseDouble(Meta("BoardOriginY") ?? "37.58"),
            ParseDouble(Meta("PcbThicknessMm") ?? "1.6"),
            bool.TryParse(Meta("MirrorBottomLayers"), out var mirror) && mirror,
            new BomColumnMapping(BomColumn("Designator"), BomColumn("Value"), BomColumn("Footprint")),
            new PnpColumnMapping(PnpColumn("Designator"), PnpColumn("X"), PnpColumn("Y"), PnpColumn("Rotation"), PnpColumn("Side")),
            components,
            fiducials,
            layerRoles);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static double ParseDouble(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string Join(params string[] fields) => string.Join(",", fields.Select(Escape));

    private static string Escape(string field) =>
        field.Contains(',') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
}
