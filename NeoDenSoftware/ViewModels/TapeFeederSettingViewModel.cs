using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeoDenSoftware.Feeders;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One tape feeder slot: an editable X/Y/Angle (world mm, same frame as the other fixtures)
/// backing a persistent <see cref="Visual"/> whose placement transform is rebuilt whenever a
/// value changes - so editing this from the Settings tab moves the feeder live on the canvas.
/// </summary>
public sealed class TapeFeederSettingViewModel : ViewModelBase
{
    private double _x;
    private double _y;
    private double _angle;
    private string _componentLabel = string.Empty;

    private readonly TextBlock _componentLabelBlock;
    private readonly Canvas _componentImageSlot;

    public string Number { get; }
    public Canvas Visual { get; }

    /// <summary>True for one of the original <see cref="Feeders.TapeFeederLibrary.Defaults"/>
    /// slots, false for a feeder the user added via "+ Add" (Settings tab). Every feeder can be
    /// removed - this only affects *how* removal is persisted: deleting a default one has to be
    /// remembered as a tombstone (<see cref="FeederPositionStore.MarkTapeDefaultDeleted"/>, since
    /// the hardcoded default list would otherwise resurrect it on the next restart), while
    /// deleting an added one just drops its saved override.</summary>
    public bool IsDefault { get; }

    /// <summary>Value/footprint text of whatever part Auto-Assign Feeders last put on this
    /// feeder, shown next to the feeder number - empty for an unassigned or tray-feeder part.</summary>
    public string ComponentLabel
    {
        get => _componentLabel;
        set
        {
            if (!SetField(ref _componentLabel, value)) return;
            _componentLabelBlock.Text = value;
        }
    }

    public double X
    {
        get => _x;
        set
        {
            if (SetField(ref _x, value)) { UpdateTransform(); Save(); }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetField(ref _y, value)) { UpdateTransform(); Save(); }
        }
    }

    /// <summary>The tape feeder image is baked at a 90-degree orientation, so the actual applied
    /// rotation is (Angle - 90) - an Angle of 90 needs no extra rotation, and -90 reverses it.</summary>
    public double Angle
    {
        get => _angle;
        set
        {
            if (SetField(ref _angle, value)) { UpdateTransform(); Save(); }
        }
    }

    public TapeFeederSettingViewModel(string number, TapeFeederXY initial, bool isDefault = false)
    {
        Number = number;
        IsDefault = isDefault;
        _x = initial.X;
        _y = initial.Y;
        _angle = initial.Angle;

        var feederVisual = TapeFeederRenderer.BuildFeederVisual(number);
        _componentLabelBlock = feederVisual.ComponentLabel;
        _componentImageSlot = feederVisual.ComponentImageSlot;

        Visual = new Canvas { IsHitTestVisible = false };
        Visual.Children.Add(feederVisual.Root);
        UpdateTransform();
    }

    /// <summary>Called by Auto-Assign Feeders when a part lands on this feeder slot: shows the
    /// part's Value/Footprint next to the number, and draws its to-scale footprint (or a cross,
    /// for an unmatched footprint) at the X-mark spot.</summary>
    public void SetAssignedComponent(string label, FootprintDefinition footprint)
    {
        ComponentLabel = label;

        _componentImageSlot.Children.Clear();
        UIElement content = footprint.Name == FootprintLibrary.NoMatch.Name
            ? FootprintRenderer.BuildCrossVisual(2.0)
            : FootprintRenderer.BuildComponentVisual(footprint, Brushes.DeepSkyBlue);
        _componentImageSlot.Children.Add(content);
    }

    /// <summary>Clears any previously assigned component - called before each Auto-Assign
    /// Feeders run so a feeder that no longer gets a part doesn't keep showing a stale one.</summary>
    public void ClearAssignedComponent()
    {
        ComponentLabel = string.Empty;
        _componentImageSlot.Children.Clear();
    }

    private void UpdateTransform()
    {
        Visual.RenderTransform = new TransformGroup
        {
            Children =
            {
                new RotateTransform(Angle - 90),
                new TranslateTransform(X, Y),
            },
        };
    }

    /// <summary>Persists this feeder's current X/Y/Angle immediately on every edit, so a Settings
    /// tab change survives an app restart instead of only living in memory for the session - see
    /// <see cref="FeederPositionStore"/>.</summary>
    private void Save() => FeederPositionStore.SaveTapeFeeder(Number, new TapeFeederXY(X, Y, Angle));
}
