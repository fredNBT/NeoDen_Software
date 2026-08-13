using NeoDenSoftware.Models;

namespace NeoDenSoftware.Gerber;

public enum InterpolationMode
{
    Linear,
    ClockwiseArc,
    CounterclockwiseArc,
}

public sealed record ApertureDefinition(ApertureShape Shape, double WidthMm, double HeightMm);
