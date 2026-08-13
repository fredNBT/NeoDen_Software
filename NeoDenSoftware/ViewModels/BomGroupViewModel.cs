using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One row in the BOM panel: every <see cref="ComponentViewModel"/> sharing the same
/// (Value, FootprintText, Side) collapses into a single group row - "Ref" lists every member's
/// designator comma-separated, and editing the footprint dropdown or removing the row applies to
/// every member at once. Grouped by the same key <c>MainViewModel.AutoAssignFeeders</c> already
/// uses, so a BOM panel group and a feeder-assignment group always agree on what counts as "the
/// same part".
/// </summary>
public sealed class BomGroupViewModel : ViewModelBase
{
    private const double PreviewSize = 40;

    public IReadOnlyList<ComponentViewModel> Members { get; }
    public string DesignatorsText { get; }
    public string? Value { get; }
    public string? FootprintText { get; }
    public BoardSide Side { get; }
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
            RebuildPreview();
        }
    }

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

    public bool UseHighFeederBank
    {
        get => Members[0].UseHighFeederBank;
        set
        {
            if (Members[0].UseHighFeederBank == value) return;
            foreach (var member in Members)
                member.UseHighFeederBank = value;
            OnPropertyChanged();
        }
    }

    public BomGroupViewModel(IReadOnlyList<ComponentViewModel> members)
    {
        Members = members;
        DesignatorsText = string.Join(", ", members.Select(m => m.Designator));
        Value = members[0].Value;
        FootprintText = members[0].FootprintText;
        Side = members[0].Side;

        var placedCount = members.Count(m => m.HasPlacement);
        PlacementStatus = placedCount == members.Count ? "Placed"
            : placedCount == 0 ? "Not on board"
            : $"Placed ({placedCount}/{members.Count})";

        // FeederNumber/UseHighFeederBank can also be set directly on a member outside this
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
        if (e.PropertyName is nameof(ComponentViewModel.FeederNumber) or nameof(ComponentViewModel.UseHighFeederBank))
            OnPropertyChanged(e.PropertyName);
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
