using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeoDenSoftware.Feeders;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One tray feeder slot's editable position/grid data (world mm, same frame as the other
/// fixtures), shown on the Settings tab. Carries a small canvas <see cref="Visual"/> showing the
/// assigned component's to-scale image, repeated <see cref="Columns"/> times and evenly spaced
/// between (BeginX, BeginY) and (EndX, BeginY), plus a text label at the tray's center (20mm above
/// it on Y) showing the feeder's own id and the assigned component's Value - one image per
/// physical slot along the tray's row, not just a single image at the start point. There's no
/// physical tray body drawing (no photo/renderer for that was ever requested), just the component
/// preview(s) and this label.
/// </summary>
public sealed class TrayFeederSettingViewModel : ViewModelBase
{
    private const double LabelOffsetYMm = 20;

    private double _beginX;
    private double _beginY;
    private double _endX;
    private double _endY;
    private int _rows;
    private int _columns;

    private FootprintDefinition? _assignedFootprint;
    private string? _assignedValue;

    private readonly Canvas _imagesHost = new();
    private readonly TextBlock _label = new()
    {
        FontSize = 4,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.Black,
    };

    public int FeederId { get; }
    public Canvas Visual { get; } = new() { IsHitTestVisible = false };

    public double BeginX
    {
        get => _beginX;
        set
        {
            if (SetField(ref _beginX, value)) { RebuildImages(); UpdateLabel(); Save(); }
        }
    }

    public double BeginY
    {
        get => _beginY;
        set
        {
            if (SetField(ref _beginY, value)) { RebuildImages(); UpdateLabel(); Save(); }
        }
    }

    public double EndX
    {
        get => _endX;
        set
        {
            if (SetField(ref _endX, value)) { RebuildImages(); UpdateLabel(); Save(); }
        }
    }

    public double EndY
    {
        get => _endY;
        set
        {
            if (SetField(ref _endY, value)) { UpdateLabel(); Save(); }
        }
    }

    public int Rows
    {
        get => _rows;
        set
        {
            if (SetField(ref _rows, value)) Save();
        }
    }

    public int Columns
    {
        get => _columns;
        set
        {
            if (SetField(ref _columns, value)) { RebuildImages(); Save(); }
        }
    }

    public TrayFeederSettingViewModel(TrayFeederPosition position)
    {
        FeederId = position.FeederId;
        _beginX = position.BeginX;
        _beginY = position.BeginY;
        _endX = position.EndX;
        _endY = position.EndY;
        _rows = position.Rows;
        _columns = position.Columns;

        Visual.Children.Add(_imagesHost);
        Visual.Children.Add(_label);
        UpdateLabel();
    }

    /// <summary>Called by Auto-Assign Feeders when a part lands on this tray feeder slot: draws
    /// its to-scale footprint (or a cross, for an unmatched footprint) once per column, evenly
    /// spaced between the tray's start (BeginX) and end (EndX) X positions, and updates the
    /// center label to show this part's Value alongside the feeder's own id.</summary>
    public void SetAssignedComponent(string? value, FootprintDefinition footprint)
    {
        _assignedFootprint = footprint;
        _assignedValue = value;
        RebuildImages();
        UpdateLabel();
    }

    /// <summary>Clears any previously assigned component - called before each Auto-Assign
    /// Feeders run so a tray that no longer gets a part doesn't keep showing a stale image or
    /// Value.</summary>
    public void ClearAssignedComponent()
    {
        _assignedFootprint = null;
        _assignedValue = null;
        RebuildImages();
        UpdateLabel();
    }

    private void RebuildImages()
    {
        _imagesHost.Children.Clear();
        if (_assignedFootprint is null || Columns <= 0) return;

        for (var i = 0; i < Columns; i++)
        {
            // A single column has nowhere to "spread" to - anchor it at the start point, same as
            // this view's original single-image behavior. Two or more columns are spaced evenly
            // across the full BeginX..EndX span, inclusive of both endpoints.
            var x = Columns == 1 ? BeginX : BeginX + i * (EndX - BeginX) / (Columns - 1);

            UIElement content = _assignedFootprint.Name == FootprintLibrary.NoMatch.Name
                ? FootprintRenderer.BuildCrossVisual(2.0)
                : FootprintRenderer.BuildComponentVisual(_assignedFootprint, Brushes.DeepSkyBlue);
            content.RenderTransform = new TranslateTransform(x, BeginY);
            _imagesHost.Children.Add(content);
        }
    }

    /// <summary>Positions the label at the tray's center - the midpoint of (BeginX,BeginY) and
    /// (EndX,EndY) - offset <see cref="LabelOffsetYMm"/> further up on Y, always showing this
    /// feeder's own id so an empty tray is still identifiable, with the assigned component's
    /// Value appended on a second line once one is assigned.</summary>
    private void UpdateLabel()
    {
        _label.Text = string.IsNullOrEmpty(_assignedValue) ? FeederId.ToString() : $"{FeederId}\n{_assignedValue}";

        var centerX = (BeginX + EndX) / 2;
        var centerY = (BeginY + EndY) / 2 + LabelOffsetYMm;
        _label.RenderTransform = new TransformGroup
        {
            // Counter-flip first (local space) so the text reads upright under the shared
            // FixturesHost Y-flip transform, then move it out to its world position - same
            // "flip then translate" order used throughout this app for world-placed text/images.
            Children = { new ScaleTransform(1, -1), new TranslateTransform(centerX, centerY) },
        };
    }

    /// <summary>Persists this tray's current Begin/End/Rows/Columns immediately on every edit, so
    /// a Settings tab change survives an app restart instead of only living in memory for the
    /// session - see <see cref="FeederPositionStore"/>.</summary>
    private void Save() => FeederPositionStore.SaveTrayFeeder(new TrayFeederPosition(FeederId, BeginX, BeginY, EndX, EndY, Rows, Columns));
}
