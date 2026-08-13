using System.IO;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Import;

public sealed class BomImportService
{
    private static readonly string[] DesignatorNames = ["Reference", "References", "Designator", "RefDes"];
    private static readonly string[] ValueNames = ["Value", "Comment"];
    private static readonly string[] FootprintNames = ["Footprint", "Package", "Footprint/Package"];

    /// <summary>Reads just the column names and a best-effort guess at which one maps to each
    /// field, for the user to confirm/override before parsing.</summary>
    public (IReadOnlyList<string> Columns, BomColumnMapping Guess) ReadHeader(string filePath)
    {
        var lines = ReadNonEmptyLines(filePath);
        if (lines.Count == 0) return (Array.Empty<string>(), new BomColumnMapping(null, null, null));

        var headerIndex = CsvParser.FindHeaderRowIndex(lines, fields => CsvParser.FindColumn(fields, DesignatorNames) >= 0);
        if (headerIndex < 0) headerIndex = 0;

        var header = CsvParser.ParseLine(lines[headerIndex]);
        var guess = new BomColumnMapping(
            ColumnNameAt(header, CsvParser.FindColumn(header, DesignatorNames)),
            ColumnNameAt(header, CsvParser.FindColumn(header, ValueNames)),
            ColumnNameAt(header, CsvParser.FindColumn(header, FootprintNames)));

        return (header, guess);
    }

    public List<BomEntry> Parse(string filePath, BomColumnMapping mapping)
    {
        if (mapping.DesignatorColumn is null) return [];

        var lines = ReadNonEmptyLines(filePath);
        if (lines.Count == 0) return [];

        var headerIndex = CsvParser.FindHeaderRowIndexForColumns(lines,
            [mapping.DesignatorColumn, mapping.ValueColumn, mapping.FootprintColumn]);
        if (headerIndex < 0) headerIndex = 0;

        var header = CsvParser.ParseLine(lines[headerIndex]);
        var designatorCol = CsvParser.FindColumn(header, mapping.DesignatorColumn);
        var valueCol = mapping.ValueColumn is null ? -1 : CsvParser.FindColumn(header, mapping.ValueColumn);
        var footprintCol = mapping.FootprintColumn is null ? -1 : CsvParser.FindColumn(header, mapping.FootprintColumn);

        if (designatorCol < 0) return [];

        var entries = new List<BomEntry>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            var fields = CsvParser.ParseLine(line);
            if (designatorCol >= fields.Length) continue;

            var value = valueCol >= 0 && valueCol < fields.Length ? fields[valueCol] : null;
            var footprint = footprintCol >= 0 && footprintCol < fields.Length ? fields[footprintCol] : null;

            foreach (var designator in SplitDesignators(fields[designatorCol]))
                entries.Add(new BomEntry(designator, value, footprint));
        }

        return entries;
    }

    private static List<string> ReadNonEmptyLines(string filePath) =>
        File.ReadAllLines(filePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    private static string? ColumnNameAt(string[] header, int index) =>
        index >= 0 && index < header.Length ? header[index] : null;

    private static IEnumerable<string> SplitDesignators(string field) =>
        field.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
