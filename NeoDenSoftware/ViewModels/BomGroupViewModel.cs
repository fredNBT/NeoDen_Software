using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One row in the BOM panel: every <see cref="ComponentViewModel"/> sharing the same
/// (Value, FootprintText, Side) collapses into a single group row - "Ref" lists every member's
/// designator comma-separated, and editing the footprint dropdown or removing the row applies to
/// every member at once. Grouped by the same key <c>MainViewModel.AutoAssignFeeders</c> already
/// uses, so a BOM panel group and a feeder-assignment group always agree on what counts as "the
/// same part" - **unless** the user manually merged otherwise-different groups together via the
/// Feeder Setup tab's Combine feature (<c>MainViewModel.CombineGroups</c>), in which case Members
/// can legitimately disagree on Value/Footprint/Side; <see cref="Value"/>/<see cref="FootprintText"/>/
/// <see cref="SideText"/> show every distinct value in that case instead of silently picking one.
/// </summary>
public sealed class BomGroupViewModel : ViewModelBase
{
    private const double PreviewSize = 40;

    public IReadOnlyList<ComponentViewModel> Members { get; }
    public string DesignatorsText { get; }
    public string? Value { get; }
    public string? FootprintText { get; }
    public BoardSide Side { get; }
    public string SideText { get; }
    public string PlacementStatus { get; }
    public Canvas PreviewHost { get; } = new() { Width = PreviewSize, Height = PreviewSize, IsHitTestVisible = false };

    /// <summary>Setting this propagates to every member so the whole group stays on one
    /// footprint, matching the "one row = one type" premise of this grouped view.</summary>
    public FootprintDefinition SelectedFootprint
    {
        get => Members[0].SelectedFootprint;
        set
        {
            if (ReferenceEquals(Members[0].SelectedFootprint, value)) return;
            foreach (var member in Members)
                member.SelectedFootprint = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FootprintDisplayText));
            RebuildPreview();
        }
    }

    /// <summary>What the BOM grid's Footprint cell actually displays: the matched footprint's
    /// name normally, or - when nothing matched - "(No Match)" plus the raw footprint text the
    /// BOM import originally found, so the user can see what failed to match instead of just a
    /// bare "(No Match)" with no clue why.</summary>
    public string FootprintDisplayText =>
        SelectedFootprint.Name == FootprintLibrary.NoMatch.Name && !string.IsNullOrEmpty(FootprintText)
            ? $"{SelectedFootprint.Name} - {FootprintText}"
            : SelectedFootprint.Name;

    /// <summary>Feeder Setup tab pass-throughs, editable one row per type just like the BOM
    /// panel's <see cref="SelectedFootprint"/> above - setting either propagates to every member
    /// so the whole group (which may span both Top and Bottom, since
    /// <c>MainViewModel.AutoAssignFeeders</c> groups by Value+Footprint only, not Side) shares one
    /// feeder assignment.</summary>
    public int? FeederNumber
    {
        get => Members[0].FeederNumber;
        set
        {
            if (Members[0].FeederNumber == value) return;
            foreach (var member in Members)
                member.FeederNumber = value;
            OnPropertyChanged();
        }
    }

    public bool UseTrayFeeder
    {
        get => Members[0].UseTrayFeeder;
        set
        {
            if (Members[0].UseTrayFeeder == value) return;
            foreach (var member in Members)
                member.UseTrayFeeder = value;
            OnPropertyChanged();
        }
    }

    public BomGroupViewModel(IReadOnlyList<ComponentViewModel> members)
    {
        Members = members;
        DesignatorsText = string.Join(", ", members.Select(m => m.Designator));
        // Normally every member shares the same Value/Footprint/Side (that's what defines a group)
        // and this is just members[0]'s value - but a manually "combined" group (see
        // MainViewModel.CombineGroups) can deliberately hold members that disagree, so join the
        // distinct values instead of silently showing only the first member's.
        Value = JoinDistinct(members.Select(m => m.Value));
        FootprintText = JoinDistinct(members.Select(m => m.FootprintText));
        Side = members[0].Side;
        SideText = members.Select(m => m.Side).Distinct().Count() == 1 ? Side.ToString() : "Mixed";

        var placedCount = members.Count(m => m.HasPlacement);
        PlacementStatus = placedCount == members.Count ? "Placed"
            : placedCount == 0 ? "Not on board"
            : $"Placed ({placedCount}/{members.Count})";

        // FeederNumber/UseTrayFeeder can also be set directly on a member outside this
        // group's own setters - e.g. MainViewModel.AutoAssignFeeders writes ComponentViewModel
        // fields straight across the whole (Value, Footprint) group it computes itself, which
        // doesn't know or care about this narrower (Value, Footprint, Side) BOM/Feeder-Setup
        // grouping. Without forwarding Members[0]'s own change notifications, the Feeder Setup
        // DataGrid (bound to this group's properties) would keep showing stale/blank values after
        // Auto-Assign runs, since nothing ever told it to re-read them.
        Members[0].PropertyChanged += OnPrimaryMemberPropertyChanged;

        RebuildPreview();
    }

    /// <summary>Call when this group is discarded (e.g. <c>MainViewModel.RebuildBomGroups</c>
    /// replacing the whole <c>BomGroups</c> collection) so the member doesn't keep a dangling
    /// reference to a group nobody is displaying anymore.</summary>
    public void Detach() => Members[0].PropertyChanged -= OnPrimaryMemberPropertyChanged;

    private void OnPrimaryMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComponentViewModel.FeederNumber) or nameof(ComponentViewModel.UseTrayFeeder))
            OnPropertyChanged(e.PropertyName);
    }

    private static string? JoinDistinct(IEnumerable<string?> values)
    {
        var distinct = values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 0 ? null : string.Join(" / ", distinct);
    }

    private void RebuildPreview()
    {
        PreviewHost.Children.Clear();
        PreviewHost.Children.Add(new Rectangle { Width = PreviewSize, Height = PreviewSize, Fill = GerberRenderer.CanvasBackground });

        var color = Side == BoardSide.Top ? Brushes.DeepSkyBlue : Brushes.Orange;
        var preview = Members[0].BuildScaledPreview(PreviewSize - 4, color);
        Canvas.SetLeft(preview, 2);
        Canvas.SetTop(preview, 2);
        PreviewHost.Children.Add(preview);
    }
}
