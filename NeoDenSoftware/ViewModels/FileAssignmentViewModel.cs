using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

public sealed class FileAssignmentViewModel : ViewModelBase
{
    private GerberLayerRole _role;

    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public string? DetectionReason { get; init; }

    public GerberLayerRole Role
    {
        get => _role;
        set => SetField(ref _role, value);
    }
}
