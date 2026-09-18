using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NeoDenSoftware.Gerber;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// Renders a small preview bitmap of a single extracted Gerber file - used by the "Confirm Layer
/// Assignments" dialog so the user can see what's actually in each file (paste pattern, mask
/// openings, board outline, or nothing recognizable) before deciding its role, rather than judging
/// purely from the filename/detection-reason text. Not tied to any confirmed role - parses the
/// file with its own best-guess <see cref="GerberLayerRole"/> (only used to decide whether to draw
/// a filled outline, same as the real board view) and returns null for anything that isn't a
/// recognizable Gerber drawing (a drill file, job file, lock file, or a genuinely empty layer) -
/// callers should just show a blank cell in that case rather than treating it as an error.
/// </summary>
public static class GerberThumbnailRenderer
{
    private static readonly Brush PreviewBrush = Brushes.DeepSkyBlue;

    public static BitmapSource? BuildThumbnail(string filePath, GerberLayerRole role, int width = 64, int height = 40)
    {
        ParsedLayer layer;
        try
        {
            layer = new GerberParser().Parse(filePath, role);
        }
        catch
        {
            // Not a Gerber file this parser understands (drill/job/lock files, or something
            // malformed) - no preview, not a crash.
            return null;
        }

        if (layer.Primitives.Count == 0 || layer.Bounds.MaxX < layer.Bounds.MinX)
            return null;

        var visual = (Canvas)GerberRenderer.BuildLayerElement(layer, PreviewBrush);

        // Uniform scale-to-fit with an 8% margin, then center the layer's own bounds-center in the
        // thumbnail and flip Y (Gerber is Y-up, screen space is Y-down) - explicit transform on
        // known bounds rather than a Viewbox around this Canvas, which would render nothing (a
        // bare Canvas always reports (0,0) DesiredSize regardless of its children - see the 8th
        // round's black-square thumbnail bug for the footprint preview equivalent of this trap).
        var boundsWidth = Math.Max(layer.Bounds.Width, 0.01);
        var boundsHeight = Math.Max(layer.Bounds.Height, 0.01);
        var scale = Math.Min(width * 0.84 / boundsWidth, height * 0.84 / boundsHeight);
        var centerX = (layer.Bounds.MinX + layer.Bounds.MaxX) / 2;
        var centerY = (layer.Bounds.MinY + layer.Bounds.MaxY) / 2;

        visual.RenderTransform = new TransformGroup
        {
            Children =
            {
                new TranslateTransform(-centerX, -centerY),
                new ScaleTransform(scale, -scale),
                new TranslateTransform(width / 2.0, height / 2.0),
            },
        };

        var host = new Canvas { Width = width, Height = height, Background = GerberRenderer.CanvasBackground };
        host.Children.Add(visual);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);
        target.Freeze();
        return target;
    }
}
