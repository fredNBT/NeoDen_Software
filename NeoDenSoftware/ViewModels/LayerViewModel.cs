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
