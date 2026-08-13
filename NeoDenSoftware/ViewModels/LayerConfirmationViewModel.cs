using System.Collections.ObjectModel;
using NeoDenSoftware.Models;

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
            Role = f.DetectedRole is GerberLayerRole.TopPaste or GerberLayerRole.BottomPaste
                or GerberLayerRole.TopSoldermask or GerberLayerRole.BottomSoldermask or GerberLayerRole.Outline
                ? f.DetectedRole
                : GerberLayerRole.Ignore,
        }));
    }
}
