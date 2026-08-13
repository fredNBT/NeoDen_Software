using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// Renders the tape feeder photo (Assets/TapeFeeder.png) scaled uniformly to a fixed height,
/// positioned so a specific reference point on the feeder (marked with an X in the source
/// image, over the tape-advance mechanism) lands exactly at local (0,0) - callers then place
/// that origin at a feeder's world X/Y via a rotate+translate transform, so the X mark is what
/// actually gets positioned/rotated about, not the image's bounding-box center.
/// </summary>
public static class TapeFeederRenderer
{
    private const double HeightMm = 14.0;

    // Source image is 801x121px; the X mark's center was located by scanning for near-black
    // pixels in that region and confirmed visually by cropping around it.
    private const double ImagePixelWidth = 801.0;
    private const double ImagePixelHeight = 121.0;
    private const double XMarkFractionX = 0.318;
    private const double XMarkFractionY = 0.537;

    public static readonly double WidthMm = HeightMm * (ImagePixelWidth / ImagePixelHeight);

    // The explicit "AssemblyName;component/..." form resolves regardless of which executable is
    // actually running (unlike the bare "application:,,,/..." form, which relies on
    // Application.ResourceAssembly defaulting to the entry assembly - not true for e.g. a
    // separate test harness that references this assembly as a library).
    //
    // Frozen so the single shared instance can be read from any thread - a BitmapImage is
    // otherwise thread-affine to whichever Dispatcher first touches it, which breaks the moment
    // more than one thread ever builds a feeder visual (as happens in multi-threaded smoke tests).
    private static readonly Lazy<BitmapImage> ImageSource = new(() =>
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri("pack://application:,,,/NeoDenSoftware;component/Assets/TapeFeeder.png");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    });

    /// <summary>Result of building a feeder's visual: the root element to add to the canvas, plus
    /// handles to the parts that get updated later by auto-assign feeders without rebuilding the
    /// whole visual - the component-label TextBlock, and a slot Canvas sitting exactly at the
    /// X-mark spot (local (0,0)) that gets the assigned part's to-scale footprint drawing.</summary>
    public sealed class FeederVisual
    {
        public required UIElement Root { get; init; }
        public required TextBlock ComponentLabel { get; init; }
        public required Canvas ComponentImageSlot { get; init; }
    }

    /// <summary>Builds the feeder image + number label, centered (in the X-mark sense described
    /// above) at local (0,0). The image is baked at a 90-degree orientation, so callers should
    /// rotate the result by (feederAngle - 90) to match a feeder's actual world angle.</summary>
    public static FeederVisual BuildFeederVisual(string number)
    {
        var image = new Image
        {
            Source = ImageSource.Value,
            Width = WidthMm,
            Height = HeightMm,
            Stretch = Stretch.Fill,
            // The image renders top-down internally; flip it back upright under the shared
            // LayerHost/FixturesHost Y-flip transform (same trick used for axis labels).
            RenderTransform = new ScaleTransform(1, -1),
        };
        Canvas.SetLeft(image, -XMarkFractionX * WidthMm);
        Canvas.SetTop(image, XMarkFractionY * HeightMm);

        var label = new TextBlock
        {
            Text = number,
            FontSize = 4,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            RenderTransform = new ScaleTransform(1, -1),
        };
        Canvas.SetLeft(label, (1 - XMarkFractionX) * WidthMm + 2);
        Canvas.SetTop(label, XMarkFractionY * HeightMm - HeightMm / 2);

        // Empty until Auto-Assign Feeders fills it in with the assigned part's Value/Footprint -
        // positioned just below the number label (larger Canvas.Top = further down on screen here,
        // since the local ScaleTransform(1,-1) above cancels the outer world Y-flip).
        var componentLabel = new TextBlock
        {
            Text = string.Empty,
            FontSize = 3,
            Foreground = Brushes.LightGray,
            RenderTransform = new ScaleTransform(1, -1),
        };
        Canvas.SetLeft(componentLabel, (1 - XMarkFractionX) * WidthMm + 2);
        Canvas.SetTop(componentLabel, XMarkFractionY * HeightMm - HeightMm / 2 + 5);

        // Empty until Auto-Assign Feeders fills it in - sits exactly at local (0,0), which is the
        // X-mark spot by construction (see the image placement above), no offset needed. Drawn
        // last (on top) so a small part is legible over the feeder photo underneath it.
        var componentImageSlot = new Canvas { RenderTransform = new ScaleTransform(1, -1) };

        var host = new Canvas { IsHitTestVisible = false };
        host.Children.Add(image);
        host.Children.Add(label);
        host.Children.Add(componentLabel);
        host.Children.Add(componentImageSlot);
        return new FeederVisual { Root = host, ComponentLabel = componentLabel, ComponentImageSlot = componentImageSlot };
    }
}
