using System.Collections.ObjectModel;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

public sealed class LayerConfirmationViewModel : ViewModelBase
{
    public ObservableCollection<FileAssignmentViewModel> Files { get; }

    public LayerConfirmationViewModel(IEnumerable<ExtractedFile> files)
    {
        Files = new ObservableCollection<FileAssignmentViewModel>(files.Select(f => new FileAssignmentViewModel
        {
            FileName = f.FileName,
            FullPath = f.FullPath,
            DetectionReason = f.DetectionReason,
            // Rendered from the file's own best-guess role (not the coerced dropdown default
            // below) purely so an outline file still previews filled, same as the real board view.
            Thumbnail = GerberThumbnailRenderer.BuildThumbnail(f.FullPath, f.DetectedRole),
            Role = f.DetectedRole is GerberLayerRole.TopPaste or GerberLayerRole.BottomPaste
                or GerberLayerRole.TopSoldermask or GerberLayerRole.BottomSoldermask or GerberLayerRole.Outline
                ? f.DetectedRole
                : GerberLayerRole.Ignore,
        }));
    }
}
