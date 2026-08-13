namespace NeoDenSoftware.Models;

public sealed class ExtractedFile
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public GerberLayerRole DetectedRole { get; set; } = GerberLayerRole.Unknown;
    public string? DetectionReason { get; set; }
}
