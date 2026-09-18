using System.Collections.ObjectModel;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Footprints;

/// <summary>
/// Built-in reference dimensions for common SMD/THT packages, used to draw a to-scale
/// placeholder outline for each placed component. Dimensions are typical/nominal values
/// (body length x width x height, mm), not tied to any single manufacturer's datasheet.
/// User-added footprints (Footprint Library window) are loaded from <see cref="CustomFootprintStore"/>
/// and appended alongside these at startup, then further additions are appended live.
/// </summary>
public static class FootprintLibrary
{
    private static readonly FootprintDefinition[] BuiltIn =
    [
        new("0201", 0.6, 0.3, 0.3, FootprintShapeKind.TwoTerminalChip, 2),
        new("0402", 1.0, 0.5, 0.5, FootprintShapeKind.TwoTerminalChip, 2),
        new("0603", 1.6, 0.8, 0.6, FootprintShapeKind.TwoTerminalChip, 2),
        new("0805", 2.0, 1.25, 0.6, FootprintShapeKind.TwoTerminalChip, 2),
        new("1206", 3.2, 1.6, 0.6, FootprintShapeKind.TwoTerminalChip, 2),
        new("1210", 3.2, 2.5, 0.6, FootprintShapeKind.TwoTerminalChip, 2),
        new("1812", 4.5, 3.2, 0.6, FootprintShapeKind.TwoTerminalChip, 2),
        new("2512", 6.4, 3.2, 0.6, FootprintShapeKind.TwoTerminalChip, 2),

        new("SOT-23", 2.9, 1.3, 1.1, FootprintShapeKind.Sot, 3),
        new("SOT-23-5", 2.9, 1.6, 1.1, FootprintShapeKind.Sot, 5),
        new("SOT-89", 4.5, 2.5, 1.5, FootprintShapeKind.Sot, 3),

        new("SOD-123", 2.7, 1.6, 1.1, FootprintShapeKind.Diode, 2),
        new("SMA", 4.3, 2.6, 1.7, FootprintShapeKind.Diode, 2),
        new("SMB", 4.3, 3.1, 2.2, FootprintShapeKind.Diode, 2),
        new("SMC", 6.7, 3.1, 2.3, FootprintShapeKind.Diode, 2),

        new("SOIC-8", 4.9, 3.9, 1.75, FootprintShapeKind.Soic, 8),
        new("SOIC-14", 8.7, 3.9, 1.75, FootprintShapeKind.Soic, 14),
        new("SOIC-16", 9.9, 3.9, 1.75, FootprintShapeKind.Soic, 16),
        new("TSSOP-8", 3.0, 4.4, 1.2, FootprintShapeKind.Soic, 8),
        new("TSSOP-16", 5.0, 4.4, 1.2, FootprintShapeKind.Soic, 16),

        new("QFN-16", 3.0, 3.0, 0.9, FootprintShapeKind.Qfn, 16),
        new("QFN-32", 5.0, 5.0, 0.9, FootprintShapeKind.Qfn, 32),
        new("QFP-32", 7.0, 7.0, 1.4, FootprintShapeKind.Qfp, 32),
        new("QFP-44", 10.0, 10.0, 1.6, FootprintShapeKind.Qfp, 44),

        new("DIP-8", 9.8, 6.3, 5.1, FootprintShapeKind.Dip, 8),

        new("Electrolytic-5mm", 5.0, 5.0, 5.4, FootprintShapeKind.Electrolytic, 2),
    ];

    /// <summary>Every known footprint - built-in plus any the user has added via the Footprint
    /// Library window. An <see cref="ObservableCollection{T}"/> so the BOM panel's footprint
    /// dropdown (bound to <see cref="AllWithNoMatch"/> via x:Static in MainWindow.xaml) picks up
    /// newly added entries live - WPF's ItemsControl subscribes to CollectionChanged on whatever
    /// object is assigned to ItemsSource regardless of how that reference was obtained, so this
    /// works even though x:Static isn't a live Binding.</summary>
    public static ObservableCollection<FootprintDefinition> All { get; } =
        new(BuiltIn.Concat(CustomFootprintStore.LoadAll()));

    /// <summary>Sentinel selected when a BOM footprint couldn't be matched (or the user picks it
    /// explicitly) - rendered as a cross marker instead of a to-scale outline.</summary>
    public static FootprintDefinition NoMatch { get; } =
        new("(No Match)", 0, 0, 0, FootprintShapeKind.TwoTerminalChip, 0);

    /// <summary>Dropdown source for the BOM panel: "(No Match)" first, then every known footprint.</summary>
    public static ObservableCollection<FootprintDefinition> AllWithNoMatch { get; } =
        new(new[] { NoMatch }.Concat(All));

    /// <summary>Adds a new user-defined footprint (with an uploaded image), persists it to disk
    /// via <see cref="CustomFootprintStore"/> so it survives app restarts, and appends it to both
    /// live collections above so every dropdown/list already bound to them updates immediately.</summary>
    public static FootprintDefinition AddCustom(string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string sourceImagePath)
    {
        var footprint = CustomFootprintStore.AddFootprint(name, aliases, lengthMm, widthMm, heightMm, sourceImagePath);
        All.Add(footprint);
        AllWithNoMatch.Add(footprint);
        return footprint;
    }

    /// <summary>Same as <see cref="AddCustom"/>, but the footprint's image is a top-down render
    /// baked once from an uploaded STL model rather than a photo the user picked directly - see
    /// <see cref="CustomFootprintStore.AddFootprintFromStl"/>. Everywhere else in the app that
    /// draws a footprint (board view, BOM thumbnails, feeder previews) only ever looks at the
    /// resulting <see cref="FootprintDefinition.ImagePath"/>, so nothing downstream needs to know
    /// this one came from an STL.</summary>
    public static FootprintDefinition AddCustomFromStl(string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string sourceStlPath)
    {
        var footprint = CustomFootprintStore.AddFootprintFromStl(name, aliases, lengthMm, widthMm, heightMm, sourceStlPath);
        All.Add(footprint);
        AllWithNoMatch.Add(footprint);
        return footprint;
    }

    /// <summary>Updates an existing custom footprint's name/dimensions (and optionally its image),
    /// persists the change, and replaces it in place in both live collections - an
    /// <see cref="ObservableCollection{T}"/> indexer assignment raises a Replace notification, so
    /// any DataGrid/dropdown row showing the old values updates immediately. Note this does *not*
    /// retroactively update components already placed on the board using the old definition -
    /// <see cref="Models.FootprintDefinition"/> is an immutable record and a placed component
    /// holds a direct reference to the instance it was matched/selected with, not a live
    /// by-name lookup; only future selections/imports see the edited values.</summary>
    public static FootprintDefinition UpdateCustom(FootprintDefinition existing, string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string? newSourceImagePath)
    {
        var updated = CustomFootprintStore.UpdateFootprint(existing.Name, name, aliases, lengthMm, widthMm, heightMm, newSourceImagePath);
        ReplaceInLiveCollections(existing, updated);
        return updated;
    }

    /// <summary>Same as <see cref="UpdateCustom"/>, but replacing the footprint's image with a
    /// fresh render from a newly uploaded STL - see <see cref="CustomFootprintStore.UpdateFootprintFromStl"/>.</summary>
    public static FootprintDefinition UpdateCustomFromStl(FootprintDefinition existing, string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, string newSourceStlPath)
    {
        var updated = CustomFootprintStore.UpdateFootprintFromStl(existing.Name, name, aliases, lengthMm, widthMm, heightMm, newSourceStlPath);
        ReplaceInLiveCollections(existing, updated);
        return updated;
    }

    /// <summary>Adds a new user-defined footprint with no image or STL - it renders as a
    /// procedural placeholder outline (from <paramref name="shapeKind"/> and L/W/H), exactly like
    /// a built-in library entry - see <see cref="CustomFootprintStore.AddFootprintProcedural"/>.</summary>
    public static FootprintDefinition AddCustomProcedural(string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, FootprintShapeKind shapeKind)
    {
        var footprint = CustomFootprintStore.AddFootprintProcedural(name, aliases, lengthMm, widthMm, heightMm, shapeKind);
        All.Add(footprint);
        AllWithNoMatch.Add(footprint);
        return footprint;
    }

    /// <summary>Same as <see cref="UpdateCustom"/>, but leaving (or reverting to) no image/STL -
    /// see <see cref="CustomFootprintStore.UpdateFootprintProcedural"/>.</summary>
    public static FootprintDefinition UpdateCustomProcedural(FootprintDefinition existing, string name, IReadOnlyList<string> aliases, double lengthMm, double widthMm, double heightMm, FootprintShapeKind shapeKind)
    {
        var updated = CustomFootprintStore.UpdateFootprintProcedural(existing.Name, name, aliases, lengthMm, widthMm, heightMm, shapeKind);
        ReplaceInLiveCollections(existing, updated);
        return updated;
    }

    private static void ReplaceInLiveCollections(FootprintDefinition existing, FootprintDefinition updated)
    {
        var indexInAll = All.IndexOf(existing);
        if (indexInAll >= 0) All[indexInAll] = updated;

        var indexInAllWithNoMatch = AllWithNoMatch.IndexOf(existing);
        if (indexInAllWithNoMatch >= 0) AllWithNoMatch[indexInAllWithNoMatch] = updated;
    }
}
