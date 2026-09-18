using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One row in the BOM panel: a single designator, with the footprint the user can
/// override via a dropdown. If it has a PnP placement, <see cref="Visual"/> is a persistent
/// wrapper Canvas whose rotate+translate <c>RenderTransform</c> is rebuilt whenever
/// <see cref="RotationDegrees"/> changes, and whose content is swapped between the to-scale
/// footprint outline and a "no match" cross whenever <see cref="SelectedFootprint"/> changes.
/// A transparent rectangle spanning the full footprint bounding box is included so the whole
/// part is clickable, not just wherever the visible drawing happens to have fill/stroke.
/// <see cref="PreviewHost"/> is a separate, untransformed thumbnail for the BOM panel row itself.
/// </summary>
public sealed class ComponentViewModel : ViewModelBase
{
    private const double PreviewSize = 40;

    private FootprintDefinition _selectedFootprint;
    private double _rotationDegrees;
    private int? _feederNumber;
    private bool _useTrayFeeder;

    public string Designator { get; }
    public string? Value { get; }
    public string? FootprintText { get; }
    public BoardSide Side { get; }
    public bool HasPlacement { get; }
    public double XMm { get; }
    public double YMm { get; }
    public string PlacementStatus => HasPlacement ? "Placed" : "Not on board";
    public Canvas? Visual { get; }
    public Canvas PreviewHost { get; } = new() { Width = PreviewSize, Height = PreviewSize, IsHitTestVisible = false };

    /// <summary>NeoDen4 feeder slot number for this part - manually editable, or set in bulk by
    /// <c>MainViewModel.AutoAssignFeeders</c>.</summary>
    public int? FeederNumber
    {
        get => _feederNumber;
        set => SetField(ref _feederNumber, value);
    }

    /// <summary>When checked, auto-assign pulls this part's feeder number from the tray-feeder
    /// bank (54-99) instead of the default tape-feeder bank (1-40) - e.g. for tray/large-component
    /// feeders.</summary>
    public bool UseTrayFeeder
    {
        get => _useTrayFeeder;
        set => SetField(ref _useTrayFeeder, value);
    }

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set
        {
            var normalized = value % 360;
            if (normalized < 0) normalized += 360;
            if (!SetField(ref _rotationDegrees, normalized)) return;
            UpdatePlacementTransform();
        }
    }

    public FootprintDefinition SelectedFootprint
    {
        get => _selectedFootprint;
        set
        {
            if (!SetField(ref _selectedFootprint, value)) return;
            RebuildVisualContent();
            RebuildPreview();
        }
    }

    public ComponentViewModel(
        string designator,
        string? value,
        string? footprintText,
        BoardSide side,
        bool hasPlacement,
        double xMm,
        double yMm,
        double rotationDegrees,
        FootprintDefinition initialFootprint,
        Canvas? visual)
    {
        Designator = designator;
        Value = value;
        FootprintText = footprintText;
        Side = side;
        HasPlacement = hasPlacement;
        XMm = xMm;
        YMm = yMm;
        Visual = visual;
        _rotationDegrees = rotationDegrees;
        _selectedFootprint = initialFootprint;

        UpdatePlacementTransform();
        RebuildVisualContent();
        RebuildPreview();
    }

    private void UpdatePlacementTransform()
    {
        if (Visual is null) return;

        Visual.RenderTransform = new TransformGroup
        {
            Children =
            {
                new RotateTransform(RotationDegrees),
                new TranslateTransform(XMm, YMm),
            },
        };
    }

    private void RebuildVisualContent()
    {
        if (Visual is null) return;

        Visual.Children.Clear();

        var isNoMatch = SelectedFootprint.Name == FootprintLibrary.NoMatch.Name;
        var hitLength = isNoMatch ? 2.0 : SelectedFootprint.LengthMm;
        var hitWidth = isNoMatch ? 2.0 : SelectedFootprint.WidthMm;
        var hitRect = new Rectangle { Width = hitLength, Height = hitWidth, Fill = Brushes.Transparent };
        Canvas.SetLeft(hitRect, -hitLength / 2);
        Canvas.SetTop(hitRect, -hitWidth / 2);
        Visual.Children.Add(hitRect);

        var color = Side == BoardSide.Top ? Brushes.DeepSkyBlue : Brushes.Orange;
        Visual.Children.Add(BuildFootprintContent(color));
    }

    private void RebuildPreview()
    {
        PreviewHost.Children.Clear();
        PreviewHost.Children.Add(new Rectangle { Width = PreviewSize, Height = PreviewSize, Fill = GerberRenderer.CanvasBackground });

        var preview = BuildScaledPreview(PreviewSize - 4, Brushes.DeepSkyBlue);
        Canvas.SetLeft(preview, 2);
        Canvas.SetTop(preview, 2);
        PreviewHost.Children.Add(preview);
    }

    /// <summary>Builds a fresh drawing of the currently selected footprint (or a cross, if
    /// unmatched) - used for both the persistent visuals above and one-off previews like the
    /// click-selection info panel (a UIElement can only be parented once, so callers that need
    /// their own copy should call this rather than reuse <see cref="Visual"/> or <see cref="PreviewHost"/>).</summary>
    public UIElement BuildFootprintContent(Brush color) =>
        SelectedFootprint.Name == FootprintLibrary.NoMatch.Name
            ? FootprintRenderer.BuildCrossVisual(2.0)
            : FootprintRenderer.BuildComponentVisual(SelectedFootprint, color);

    /// <summary>Builds a fixed-size (<paramref name="boxSizeMm"/> x boxSizeMm), centered,
    /// scaled-to-fit preview of the current footprint. Deliberately does not use a Viewbox:
    /// <see cref="FootprintRenderer"/>'s drawings are plain Canvases with no explicit
    /// Width/Height (by design, since they're centered at local (0,0) for rotate-about-center
    /// world placement), and a bare Canvas reports a (0,0) desired size to WPF's layout system -
    /// a Viewbox scaling a (0,0)-sized child renders nothing, leaving only whatever background
    /// sits behind it visible (this is why thumbnails were showing up as a solid black square).
    /// Computing the scale directly from the known mm dimensions sidesteps layout entirely.</summary>
    public UIElement BuildScaledPreview(double boxSizeMm, Brush color)
    {
        var isNoMatch = SelectedFootprint.Name == FootprintLibrary.NoMatch.Name;
        var contentLength = isNoMatch ? 2.0 : SelectedFootprint.LengthMm;
        var contentWidth = isNoMatch ? 2.0 : SelectedFootprint.WidthMm;
        var scale = Math.Min(boxSizeMm / Math.Max(contentLength, 0.01), boxSizeMm / Math.Max(contentWidth, 0.01));

        var scaled = new Canvas { RenderTransform = new ScaleTransform(scale, scale) };
        scaled.Children.Add(BuildFootprintContent(color));

        // The footprint drawing is centered at local (0,0); placing the scaled wrapper's own
        // origin at the box's center lines the two centers up.
        var box = new Canvas { Width = boxSizeMm, Height = boxSizeMm };
        box.Children.Add(scaled);
        Canvas.SetLeft(scaled, boxSizeMm / 2);
        Canvas.SetTop(scaled, boxSizeMm / 2);

        return box;
    }
}
