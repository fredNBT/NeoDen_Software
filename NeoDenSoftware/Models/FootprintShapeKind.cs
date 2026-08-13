namespace NeoDenSoftware.Models;

public enum FootprintShapeKind
{
    TwoTerminalChip,
    Sot,
    Diode,
    Soic,
    Qfp,
    Qfn,
    Dip,
    Electrolytic,

    /// <summary>User-added footprint from the Footprint Library window - always has an
    /// <see cref="FootprintDefinition.ImagePath"/> and is rendered as that image rather than a
    /// shape-specific procedural drawing.</summary>
    Custom,
}
