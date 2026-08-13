using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Import;

public sealed partial class PnpImportService
{
    private static readonly string[] DesignatorNames = ["Designator", "RefDes", "Reference"];
    private static readonly string[] XNames = ["Mid X", "PosX", "Ref X", "Center-X(mm)", "Center-X(mil)", "Center-X", "X"];
    private static readonly string[] YNames = ["Mid Y", "PosY", "Ref Y", "Center-Y(mm)", "Center-Y(mil)", "Center-Y", "Y"];
    private static readonly string[] RotationNames = ["Rotation", "Rot"];
    private static readonly string[] SideNames = ["Layer", "Side"];

    /// <summary>Reads just the column names, a best-effort guess at which one maps to each
    /// field, and the detected unit (from a "Units used:" line, if present in any preamble
    /// text) - for the user to confirm/override before parsing.</summary>
    public (IReadOnlyList<string> Columns, PnpColumnMapping Guess, double UnitToMm) ReadHeader(string filePath)
    {
        var lines = ReadNonEmptyLines(filePath);
        if (lines.Count == 0) return (Array.Empty<string>(), new PnpColumnMapping(null, null, null, null, null), 1.0);

        var headerIndex = CsvParser.FindHeaderRowIndex(lines, fields =>
            CsvParser.FindColumn(fields, DesignatorNames) >= 0
            && CsvParser.FindColumn(fields, XNames) >= 0
            && CsvParser.FindColumn(fields, YNames) >= 0);
        if (headerIndex < 0) headerIndex = 0;

        var unitToMm = DetectUnitToMm(lines.Take(headerIndex));
        var header = CsvParser.ParseLine(lines[headerIndex]);

        var guess = new PnpColumnMapping(
            ColumnNameAt(header, CsvParser.FindColumn(header, DesignatorNames)),
            ColumnNameAt(header, CsvParser.FindColumn(header, XNames)),
            ColumnNameAt(header, CsvParser.FindColumn(header, YNames)),
            ColumnNameAt(header, CsvParser.FindColumn(header, RotationNames)),
            ColumnNameAt(header, CsvParser.FindColumn(header, SideNames)));

        return (header, guess, unitToMm);
    }

    public List<PnpEntry> Parse(string filePath, PnpColumnMapping mapping, double unitToMm)
    {
        if (mapping.DesignatorColumn is null || mapping.XColumn is null || mapping.YColumn is null) return [];

        var lines = ReadNonEmptyLines(filePath);
        if (lines.Count == 0) return [];

        var headerIndex = CsvParser.FindHeaderRowIndexForColumns(lines,
            [mapping.DesignatorColumn, mapping.XColumn, mapping.YColumn, mapping.RotationColumn, mapping.SideColumn]);
        if (headerIndex < 0) headerIndex = 0;

        var header = CsvParser.ParseLine(lines[headerIndex]);
        var designatorCol = CsvParser.FindColumn(header, mapping.DesignatorColumn);
        var xCol = CsvParser.FindColumn(header, mapping.XColumn);
        var yCol = CsvParser.FindColumn(header, mapping.YColumn);
        var rotationCol = mapping.RotationColumn is null ? -1 : CsvParser.FindColumn(header, mapping.RotationColumn);
        var sideCol = mapping.SideColumn is null ? -1 : CsvParser.FindColumn(header, mapping.SideColumn);

        if (designatorCol < 0 || xCol < 0 || yCol < 0) return [];

        var entries = new List<PnpEntry>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            var fields = CsvParser.ParseLine(line);
            if (designatorCol >= fields.Length || xCol >= fields.Length || yCol >= fields.Length) continue;
            if (!TryParseNumber(fields[xCol], out var x) || !TryParseNumber(fields[yCol], out var y)) continue;

            var rotation = rotationCol >= 0 && rotationCol < fields.Length
                && double.TryParse(fields[rotationCol].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
                ? r
                : 0.0;
            var side = sideCol >= 0 && sideCol < fields.Length ? ParseSide(fields[sideCol]) : BoardSide.Top;

            entries.Add(new PnpEntry(fields[designatorCol].Trim(), x * unitToMm, y * unitToMm, rotation, side));
        }

        return entries;
    }

    private static List<string> ReadNonEmptyLines(string filePath) =>
        File.ReadAllLines(filePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    private static string? ColumnNameAt(string[] header, int index) =>
        index >= 0 && index < header.Length ? header[index] : null;

    private static double DetectUnitToMm(IEnumerable<string> preambleLines)
    {
        foreach (var line in preambleLines)
        {
            var match = UnitsUsedRegex().Match(line);
            if (!match.Success) continue;

            return match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "mil" => 0.0254,
                "inch" or "in" => 25.4,
                _ => 1.0,
            };
        }

        return 1.0; // assume mm when unspecified
    }

    private static bool TryParseNumber(string raw, out double value)
    {
        // Tolerate a trailing unit suffix ("mm", "mil") some PnP exports include per value.
        var trimmed = raw.Trim().TrimEnd('m', 'M', 'i', 'l', 'I', 'L').Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        // Some regional exports (e.g. Altium with a European locale) use a comma as the
        // decimal separator instead of a thousands separator, e.g. "26,2890" means 26.2890.
        if (trimmed.Count(c => c == ',') == 1 && !trimmed.Contains('.'))
            return double.TryParse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        return false;
    }

    private static BoardSide ParseSide(string raw) =>
        raw.Trim().StartsWith("B", StringComparison.OrdinalIgnoreCase) ? BoardSide.Bottom : BoardSide.Top;

    [GeneratedRegex(@"Units\s*used\s*:\s*(?<unit>mm|mil|inch|in)", RegexOptions.IgnoreCase)]
    private static partial Regex UnitsUsedRegex();
}
