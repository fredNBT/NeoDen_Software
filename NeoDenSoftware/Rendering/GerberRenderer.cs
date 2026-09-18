using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// Converts a parsed Gerber layer into WPF geometry. Coordinates are left in raw
/// millimeter/Gerber space (Y-up); the caller applies one shared Y-flip + zoom/pan
/// transform to the container that hosts every layer's visual.
/// </summary>
public static class GerberRenderer
{
    public static readonly Brush CanvasBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)));

    /// <summary>The classic FR4-soldermask "PCB green" - used to fill the board outline and,
    /// for a consistent look, as that layer's stroke/legend color too.</summary>
    public static readonly Brush PcbGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x3C)));

    public static UIElement BuildLayerElement(ParsedLayer layer, Brush brush)
    {
        var host = new Canvas { IsHitTestVisible = false };

        if (layer.Role == GerberLayerRole.Outline)
            AddOutlineFill(host, layer);

        AddFilledGeometry(host, layer, brush);
        AddStrokedGeometry(host, layer, brush);

        return host;
    }

    // The outline layer is usually just a sequence of line/arc traces tracing the board's
    // perimeter (drawn with G01/G02/G03, not a G36/G37 filled region), so there's normally no
    // RegionPrimitive to fill directly. Chain the segments/arcs end-to-end into closed loops
    // and fill those instead, so the board reads as a solid shape rather than a bare outline.
    private static void AddOutlineFill(Canvas host, ParsedLayer layer)
    {
        var loops = BuildClosedLoops(layer.Primitives);
        if (loops.Count == 0) return;

        // EvenOdd so any inner loop (e.g. a board cutout or mounting-hole traced the same way)
        // punches through the fill automatically, without having to classify outer vs. inner.
        var geometryGroup = new GeometryGroup { FillRule = FillRule.EvenOdd };
        foreach (var loop in loops)
        {
            var figure = new PathFigure { StartPoint = ToPoint(loop[0]), IsClosed = true, IsFilled = true };
            for (var i = 1; i < loop.Count; i++)
                figure.Segments.Add(new LineSegment(ToPoint(loop[i]), isStroked: false));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometryGroup.Children.Add(geometry);
        }

        geometryGroup.Freeze();
        host.Children.Add(new Path { Data = geometryGroup, Fill = PcbGreen });
    }

    // Real-world outlines rarely have exactly-matching floating-point endpoints (rounding from
    // unit conversion, tessellated arcs meeting a straight segment, etc.), so matching against
    // a tiny fixed tolerance and taking the first candidate found routinely breaks the chain
    // early - each leftover fragment then gets auto-closed with a straight chord, producing a
    // wrong/partial fill instead of the one true board shape. Picking the globally CLOSEST
    // remaining endpoint at each step (not just the first one within a small tolerance) is far
    // more robust to that kind of drift while a generous max-gap still keeps genuinely separate
    // loops (e.g. a real cutout) from being bridged together.
    /// <summary>Internal (not private) so <see cref="Dxf.DxfWriter"/> can chain the same
    /// outline-file line/arc primitives into closed loops for DXF export that this class already
    /// uses to fill the board shape on screen - guarantees the exported DXF outline is exactly the
    /// same shape the user sees rendered, not a second, potentially-diverging implementation.</summary>
    internal static List<List<PointMm>> BuildClosedLoops(IReadOnlyList<GerberPrimitive> primitives, double maxGapMm = 1.0)
    {
        var segments = new List<(PointMm Start, PointMm End)>();
        foreach (var primitive in primitives)
        {
            switch (primitive)
            {
                case SegmentPrimitive s:
                    segments.Add((s.Start, s.End));
                    break;
                case ArcPrimitive a:
                    var previous = a.Start;
                    foreach (var point in TessellateArc(a))
                    {
                        segments.Add((previous, point));
                        previous = point;
                    }
                    break;
            }
        }

        var loops = new List<List<PointMm>>();
        var remaining = segments;

        while (remaining.Count > 0)
        {
            var loop = new List<PointMm> { remaining[0].Start, remaining[0].End };
            remaining.RemoveAt(0);

            bool extended;
            do
            {
                extended = false;
                var tail = loop[^1];

                var bestIndex = -1;
                var bestDistance = double.MaxValue;
                var bestIsStart = true;

                for (var i = 0; i < remaining.Count; i++)
                {
                    var startDistance = Distance(remaining[i].Start, tail);
                    if (startDistance < bestDistance)
                    {
                        bestDistance = startDistance;
                        bestIndex = i;
                        bestIsStart = true;
                    }

                    var endDistance = Distance(remaining[i].End, tail);
                    if (endDistance < bestDistance)
                    {
                        bestDistance = endDistance;
                        bestIndex = i;
                        bestIsStart = false;
                    }
                }

                if (bestIndex >= 0 && bestDistance <= maxGapMm)
                {
                    loop.Add(bestIsStart ? remaining[bestIndex].End : remaining[bestIndex].Start);
                    remaining.RemoveAt(bestIndex);
                    extended = true;
                }
            } while (extended);

            if (loop.Count >= 3)
                loops.Add(loop);
        }

        return loops;
    }

    private static void AddFilledGeometry(Canvas host, ParsedLayer layer, Brush brush)
    {
        var darkFilled = new GeometryGroup { FillRule = FillRule.Nonzero };
        var clearFilled = new GeometryGroup { FillRule = FillRule.Nonzero };

        foreach (var primitive in layer.Primitives)
        {
            switch (primitive)
            {
                case FlashPrimitive flash:
                    darkFilled.Children.Add(BuildFlashGeometry(flash));
                    break;
                case RegionPrimitive { Contour.Count: >= 3 } region:
                    (region.Clear ? clearFilled : darkFilled).Children.Add(BuildRegionGeometry(region));
                    break;
            }
        }

        if (darkFilled.Children.Count > 0)
        {
            darkFilled.Freeze();
            host.Children.Add(new Path { Data = darkFilled, Fill = brush });
        }

        if (clearFilled.Children.Count > 0)
        {
            clearFilled.Freeze();
            // Clear-polarity (pour cutout) regions are approximated by painting over with the
            // canvas background rather than true boolean subtraction from the dark geometry.
            host.Children.Add(new Path { Data = clearFilled, Fill = CanvasBackground });
        }
    }

    private static void AddStrokedGeometry(Canvas host, ParsedLayer layer, Brush brush)
    {
        var segmentsByWidth = new Dictionary<double, List<SegmentPrimitive>>();
        var arcsByWidth = new Dictionary<double, List<ArcPrimitive>>();

        foreach (var primitive in layer.Primitives)
        {
            switch (primitive)
            {
                case SegmentPrimitive seg:
                    GetOrAdd(segmentsByWidth, seg.WidthMm).Add(seg);
                    break;
                case ArcPrimitive arc:
                    GetOrAdd(arcsByWidth, arc.WidthMm).Add(arc);
                    break;
            }
        }

        foreach (var (width, segments) in segmentsByWidth)
            host.Children.Add(BuildStrokedPath(BuildSegmentGeometry(segments), brush, width));

        foreach (var (width, arcs) in arcsByWidth)
            host.Children.Add(BuildStrokedPath(BuildArcGeometry(arcs), brush, width));
    }

    private static List<T> GetOrAdd<T>(Dictionary<double, List<T>> dict, double key)
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        return list;
    }

    private static Path BuildStrokedPath(Geometry geometry, Brush brush, double width) => new()
    {
        Data = geometry,
        Stroke = brush,
        StrokeThickness = width,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Geometry BuildSegmentGeometry(List<SegmentPrimitive> segments)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var seg in segments)
            {
                ctx.BeginFigure(ToPoint(seg.Start), isFilled: false, isClosed: false);
                ctx.LineTo(ToPoint(seg.End), isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // WPF's ArcSegment picks one of two possible circles through a given start/end/radius
    // (resolved via IsLargeArc + SweepDirection), and getting that resolution consistently
    // right for Y-up Gerber data rendered through a later Y-flip transform is error-prone.
    // Tessellating with the already-known center avoids the ambiguity entirely.
    private static Geometry BuildArcGeometry(List<ArcPrimitive> arcs)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var arc in arcs)
            {
                ctx.BeginFigure(ToPoint(arc.Start), isFilled: false, isClosed: false);
                foreach (var point in TessellateArc(arc))
                    ctx.LineTo(ToPoint(point), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static IEnumerable<PointMm> TessellateArc(ArcPrimitive arc)
    {
        var radius = Distance(arc.Center, arc.Start);
        if (radius <= 0)
        {
            yield return arc.End;
            yield break;
        }

        var startAngle = Math.Atan2(arc.Start.Y - arc.Center.Y, arc.Start.X - arc.Center.X);
        var endAngle = Math.Atan2(arc.End.Y - arc.Center.Y, arc.End.X - arc.Center.X);
        var sweep = endAngle - startAngle;
        if (arc.Clockwise)
        {
            while (sweep > 0) sweep -= 2 * Math.PI;
        }
        else
        {
            while (sweep < 0) sweep += 2 * Math.PI;
        }

        const int stepsPerFullCircle = 180;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * stepsPerFullCircle));
        for (var s = 1; s <= steps; s++)
        {
            var angle = startAngle + sweep * s / steps;
            yield return new PointMm(arc.Center.X + radius * Math.Cos(angle), arc.Center.Y + radius * Math.Sin(angle));
        }
    }

    private static Geometry BuildFlashGeometry(FlashPrimitive flash) => flash.Shape switch
    {
        ApertureShape.Circle => new EllipseGeometry(ToPoint(flash.Position), flash.WidthMm / 2, flash.WidthMm / 2),
        ApertureShape.Rectangle => new RectangleGeometry(new Rect(
            flash.Position.X - flash.WidthMm / 2, flash.Position.Y - flash.HeightMm / 2,
            flash.WidthMm, flash.HeightMm)),
        ApertureShape.Obround => BuildObroundGeometry(flash),
        _ => new EllipseGeometry(ToPoint(flash.Position), flash.WidthMm / 2, flash.WidthMm / 2),
    };

    private static Geometry BuildObroundGeometry(FlashPrimitive flash)
    {
        var rect = new Rect(
            flash.Position.X - flash.WidthMm / 2, flash.Position.Y - flash.HeightMm / 2,
            flash.WidthMm, flash.HeightMm);
        var radius = Math.Min(flash.WidthMm, flash.HeightMm) / 2;
        return new RectangleGeometry(rect, radius, radius);
    }

    private static Geometry BuildRegionGeometry(RegionPrimitive region)
    {
        var figure = new PathFigure { StartPoint = ToPoint(region.Contour[0]), IsClosed = true, IsFilled = true };
        for (var i = 1; i < region.Contour.Count; i++)
            figure.Segments.Add(new LineSegment(ToPoint(region.Contour[i]), isStroked: false));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static double Distance(PointMm a, PointMm b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static Point ToPoint(PointMm p) => new(p.X, p.Y);

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
