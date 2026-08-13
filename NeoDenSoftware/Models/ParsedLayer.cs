namespace NeoDenSoftware.Models;

public readonly record struct BoundingBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public static BoundingBox Empty => new(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);

    public BoundingBox Include(PointMm p) => new(
        Math.Min(MinX, p.X), Math.Min(MinY, p.Y),
        Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public BoundingBox Include(BoundingBox other) => new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
}

public sealed class ParsedLayer
{
    public required GerberLayerRole Role { get; init; }
    public required IReadOnlyList<GerberPrimitive> Primitives { get; init; }
    public required BoundingBox Bounds { get; init; }
}
