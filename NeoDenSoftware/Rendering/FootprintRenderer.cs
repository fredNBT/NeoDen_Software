using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// Builds a simple to-scale placeholder outline for a footprint (body rectangle, end caps
/// for 2-terminal parts, and a pin-1 corner marker), centered at local (0,0) in millimeters
/// so callers can place it with a rotate-then-translate transform at a component's PnP
/// position. This is a schematic representation sized from <see cref="FootprintLibrary"/>
/// dimensions, not a photo of the real part - unless the footprint has a user-uploaded
/// <see cref="FootprintDefinition.ImagePath"/> (see the Footprint Library window), in which case
/// that image is drawn instead, still centered and scaled to the same Length x Width.
/// </summary>
public static class FootprintRenderer
{
    public static UIElement BuildComponentVisual(FootprintDefinition footprint, Brush color)
    {
        if (footprint.ImagePath is { } imagePath && File.Exists(imagePath))
            return BuildImageVisual(footprint, imagePath);

        var canvas = new Canvas { IsHitTestVisible = false };
        var halfLength = footprint.LengthMm / 2;
        var halfWidth = footprint.WidthMm / 2;

        var body = new Rectangle
        {
            Width = footprint.LengthMm,
            Height = footprint.WidthMm,
            Fill = color,
            Opacity = 0.55,
            Stroke = color,
            StrokeThickness = Math.Max(Math.Min(footprint.LengthMm, footprint.WidthMm) * 0.06, 0.02),
        };
        Canvas.SetLeft(body, -halfLength);
        Canvas.SetTop(body, -halfWidth);
        canvas.Children.Add(body);

        if (footprint.ShapeKind is FootprintShapeKind.TwoTerminalChip or FootprintShapeKind.Diode)
        {
            var capWidth = Math.Max(footprint.LengthMm * 0.22, 0.05);
            AddEndCap(canvas, -halfLength, halfWidth, capWidth);
            AddEndCap(canvas, halfLength - capWidth, halfWidth, capWidth);
        }

        AddPin1Marker(canvas, footprint, halfLength, halfWidth);

        return canvas;
    }

    /// <summary>Draws a user-uploaded footprint image scaled to Length x Width (mm), centered at
    /// local (0,0) - same placement contract as the procedural drawing above. The local
    /// ScaleTransform(1,-1) counteracts the shared world Y-flip applied by LayerHost/
    /// FixturesHost (same trick TapeFeederRenderer uses for its photo), so the uploaded image
    /// renders right-side-up rather than mirrored. Frozen so the BitmapImage is safe to read from
    /// any thread, same reasoning as TapeFeederRenderer's cached image.</summary>
    private static UIElement BuildImageVisual(FootprintDefinition footprint, string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            Width = footprint.LengthMm,
            Height = footprint.WidthMm,
            Stretch = Stretch.Fill,
            RenderTransform = new ScaleTransform(1, -1),
        };
        Canvas.SetLeft(image, -footprint.LengthMm / 2);
        Canvas.SetTop(image, footprint.WidthMm / 2);

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(image);
        return canvas;
    }

    private static void AddEndCap(Canvas canvas, double left, double halfWidth, double capWidth)
    {
        var rect = new Rectangle { Width = capWidth, Height = halfWidth * 2, Fill = Brushes.DarkSlateGray };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, -halfWidth);
        canvas.Children.Add(rect);
    }

    private static void AddPin1Marker(Canvas canvas, FootprintDefinition footprint, double halfLength, double halfWidth)
    {
        var dotDiameter = Math.Max(Math.Min(footprint.LengthMm, footprint.WidthMm) * 0.18, 0.06);
        var dot = new Ellipse { Width = dotDiameter, Height = dotDiameter, Fill = Brushes.White };
        Canvas.SetLeft(dot, -halfLength + dotDiameter * 0.5);
        Canvas.SetTop(dot, -halfWidth + dotDiameter * 0.5);
        canvas.Children.Add(dot);
    }

    /// <summary>Placeholder marker for a component whose footprint isn't known (unmatched or
    /// explicitly set to "No Match") - a black-and-white X, since there's no size to draw to scale.</summary>
    public static UIElement BuildCrossVisual(double sizeMm)
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        var half = sizeMm / 2;

        AddCrossLine(canvas, -half, -half, half, half, sizeMm);
        AddCrossLine(canvas, -half, half, half, -half, sizeMm);

        return canvas;
    }

    private static void AddCrossLine(Canvas canvas, double x1, double y1, double x2, double y2, double sizeMm)
    {
        canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.Black, StrokeThickness = sizeMm * 0.22 });
        canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.White, StrokeThickness = sizeMm * 0.09 });
    }
}
