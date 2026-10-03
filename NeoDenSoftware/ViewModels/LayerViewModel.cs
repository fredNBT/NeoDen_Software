using System.Windows;
using System.Windows.Media;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

public sealed class LayerViewModel : ViewModelBase
{
    private bool _isVisible = true;
    private double _opacity = 0.85;

    public required string Name { get; init; }
    public required GerberLayerRole Role { get; init; }
    public required Brush Color { get; init; }
    public required UIElement Visual { get; init; }

    /// <summary>False for a layer whose color is always fixed (Paste's shiny-gold gradient look -
    /// a gradient has no single flat color a picker could hand back, so it's kept as a fixed,
    /// non-editable appearance rather than offering a picker that can't actually represent it).
    /// The Layers panel uses this to skip wiring up the color-swatch click for such a layer.</summary>
    public bool IsColorEditable { get; init; } = true;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetField(ref _isVisible, value))
                Visual.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetField(ref _opacity, value))
                Visual.Opacity = value;
        }
    }
}
