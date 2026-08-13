using System.Text.RegularExpressions;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Footprints;

/// <summary>
/// Best-effort matching of a BOM's free-text footprint/package column against
/// <see cref="FootprintLibrary"/>. Real-world footprint naming varies enormously between
/// EDA tools (e.g. KiCad's "R_0603_1608Metric" vs. Altium's "SOIC127P600X175-8N"), so this
/// is heuristic, not exact - unmatched footprints simply aren't rendered.
/// </summary>
public static partial class FootprintMatcher
{
    private static readonly HashSet<string> KnownChipSizes = FootprintLibrary.All
        .Where(f => f.ShapeKind == FootprintShapeKind.TwoTerminalChip)
        .Select(f => f.Name)
        .ToHashSet();

    // Cached case-insensitive index of every Name *and* every Alias across the whole library,
    // rebuilt lazily whenever FootprintLibrary.All changes. At thousands-of-parts scale this
    // matters: an O(n) linear scan per BOM row (as this used to be) adds up fast across a big
    // import, where an O(1) dictionary lookup doesn't. TryAdd means the first-registered owner of
    // a name wins if two entries ever share one - the Footprint Library window's duplicate check
    // is the primary guard against that ever happening, this is just defense in depth.
    private static Dictionary<string, FootprintDefinition>? _nameIndex;

    static FootprintMatcher()
    {
        FootprintLibrary.All.CollectionChanged += (_, _) => _nameIndex = null;
    }

    private static Dictionary<string, FootprintDefinition> NameIndex
    {
        get
        {
            if (_nameIndex is not null) return _nameIndex;

            var index = new Dictionary<string, FootprintDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var footprint in FootprintLibrary.All)
            {
                index.TryAdd(footprint.Name.Trim(), footprint);
                foreach (var alias in footprint.Aliases)
                    index.TryAdd(alias.Trim(), footprint);
            }

            return _nameIndex = index;
        }
    }

    public static FootprintDefinition? Match(string? rawFootprint)
    {
        if (string.IsNullOrWhiteSpace(rawFootprint)) return null;

        // An exact match against any known name or alias (case-insensitive) wins over every
        // heuristic below - this is what makes a user-added custom footprint (Footprint Library
        // window) auto-place on import whenever the BOM's Footprint column literally names it (or
        // one of its aliases), rather than only being selectable manually afterward. Built-in
        // names benefit from this too, though they're usually still reached via the pattern rules
        // below for EDA-tool-specific encodings (e.g. KiCad's "R_0603_1608Metric") that don't
        // literally equal a library name.
        if (NameIndex.TryGetValue(rawFootprint.Trim(), out var exact))
            return exact;

        var normalized = rawFootprint.ToUpperInvariant();

        foreach (Match digits in DigitGroupRegex().Matches(normalized))
        {
            if (KnownChipSizes.Contains(digits.Value))
                return Find(digits.Value);
        }

        if (Contains(normalized, "SOT235") || Contains(normalized, "SOT-23-5") || Contains(normalized, "SOT23-5"))
            return Find("SOT-23-5");
        if (Contains(normalized, "SOT89") || Contains(normalized, "SOT-89"))
            return Find("SOT-89");
        if (Contains(normalized, "SOT23") || Contains(normalized, "SOT-23"))
            return Find("SOT-23");

        if (Contains(normalized, "SOD123") || Contains(normalized, "SOD-123"))
            return Find("SOD-123");
        if (Contains(normalized, "DO214AC") || IsStandaloneToken(normalized, "SMA"))
            return Find("SMA");
        if (Contains(normalized, "DO214AA") || IsStandaloneToken(normalized, "SMB"))
            return Find("SMB");
        if (Contains(normalized, "DO214AB") || IsStandaloneToken(normalized, "SMC"))
            return Find("SMC");

        if (Contains(normalized, "TSSOP"))
            return FindByPinCount("TSSOP", ExtractPinCount(normalized));
        if (Contains(normalized, "SOIC") || Contains(normalized, "SOP"))
            return FindByPinCount("SOIC", ExtractPinCount(normalized));
        if (Contains(normalized, "QFN"))
            return FindByPinCount("QFN", ExtractPinCount(normalized));
        if (Contains(normalized, "QFP"))
            return FindByPinCount("QFP", ExtractPinCount(normalized));
        if (Contains(normalized, "DIP"))
            return Find("DIP-8");

        if (Contains(normalized, "ELECTROLYTIC") || Contains(normalized, "CP_RADIAL") || Contains(normalized, "CP-RADIAL"))
            return Find("Electrolytic-5mm");

        return null;
    }

    private static FootprintDefinition? Find(string name) =>
        FootprintLibrary.All.FirstOrDefault(f => f.Name == name);

    private static FootprintDefinition? FindByPinCount(string namePrefix, int? pinCount)
    {
        var candidates = FootprintLibrary.All
            .Where(f => f.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        return pinCount is null
            ? candidates[0]
            : candidates.OrderBy(f => Math.Abs(f.PinCount - pinCount.Value)).First();
    }

    private static int? ExtractPinCount(string normalized)
    {
        var trailing = TrailingPinCountRegex().Match(normalized);
        if (trailing.Success) return int.Parse(trailing.Groups["n"].Value);

        var inline = InlinePinCountRegex().Match(normalized);
        return inline.Success ? int.Parse(inline.Groups["n"].Value) : null;
    }

    private static bool Contains(string haystack, string token) => haystack.Contains(token, StringComparison.Ordinal);

    private static bool IsStandaloneToken(string haystack, string token) =>
        Regex.IsMatch(haystack, $@"(?<![A-Z0-9]){Regex.Escape(token)}(?![A-Z0-9])");

    [GeneratedRegex(@"(?<!\d)\d{3,4}(?!\d)")]
    private static partial Regex DigitGroupRegex();

    // Matches an Altium/IPC-style trailing pin count, e.g. "SOIC127P600X175-8N" -> 8.
    [GeneratedRegex(@"-(?<n>\d{1,2})N?\b")]
    private static partial Regex TrailingPinCountRegex();

    // Matches a pin count appended directly to the package family name, e.g. "SOIC-8", "QFN32".
    [GeneratedRegex(@"(?:SOIC|TSSOP|QFN|QFP|SOP)-?(?<n>\d{1,3})")]
    private static partial Regex InlinePinCountRegex();
}
