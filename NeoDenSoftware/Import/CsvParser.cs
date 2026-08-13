using System.Text;

namespace NeoDenSoftware.Import;

public static class CsvParser
{
    public static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    public static int FindColumn(string[] header, params string[] names) =>
        Array.FindIndex(header, h => names.Any(n => string.Equals(h.Trim(), n, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Finds the first line that looks like the real column-header row, skipping any
    /// preamble text some export tools prepend (e.g. Altium's "Pick and Place Locations"
    /// title block, revision/date info, "====" separators). Returns -1 if none match.
    /// </summary>
    public static int FindHeaderRowIndex(IReadOnlyList<string> lines, Func<string[], bool> looksLikeHeader)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (looksLikeHeader(ParseLine(lines[i])))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Finds the header row that contains every one of the given (non-null) column names -
    /// used once a column mapping has been confirmed, so the exact confirmed names locate the
    /// real header row regardless of any preamble text above it.
    /// </summary>
    public static int FindHeaderRowIndexForColumns(IReadOnlyList<string> lines, IEnumerable<string?> requiredColumnNames)
    {
        var required = requiredColumnNames.Where(n => n is not null).Cast<string>().ToList();
        return required.Count == 0
            ? -1
            : FindHeaderRowIndex(lines, fields => required.All(name => FindColumn(fields, name) >= 0));
    }
}
