namespace NeoDenSoftware.Models;

public enum ApertureShape
{
    Circle,
    Rectangle,
    Obround,
}

public readonly record struct PointMm(double X, double Y);

public abstract record GerberPrimitive;

public sealed record SegmentPrimitive(PointMm Start, PointMm End, double WidthMm) : GerberPrimitive;

public sealed record ArcPrimitive(PointMm Start, PointMm End, PointMm Center, bool Clockwise, double WidthMm) : GerberPrimitive;

public sealed record FlashPrimitive(PointMm Position, ApertureShape Shape, double WidthMm, double HeightMm) : GerberPrimitive;

public sealed record RegionPrimitive(IReadOnlyList<PointMm> Contour, bool Clear) : GerberPrimitive;
