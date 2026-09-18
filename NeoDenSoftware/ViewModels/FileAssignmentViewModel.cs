using System.Windows.Media.Imaging;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

public sealed class FileAssignmentViewModel : ViewModelBase
{
    private GerberLayerRole _role;

    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public string? DetectionReason { get; init; }

    /// <summary>Small preview render of what's actually in the file (see
    /// <see cref="Rendering.GerberThumbnailRenderer"/>) - null for anything that isn't a
    /// recognizable Gerber drawing (drill/job/lock files, or a genuinely empty layer).</summary>
    public BitmapSource? Thumbnail { get; init; }

    public GerberLayerRole Role
    {
        get => _role;
        set => SetField(ref _role, value);
    }
}
