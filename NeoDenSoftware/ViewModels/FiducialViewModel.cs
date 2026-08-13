using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One fiducial mark, either found in the BOM/PnP during import (designator or value containing
/// "FID" or "Fiducial") or added manually by the user via the Layers sidebar's "+ Add" button -
/// the sidebar's fiducial rows are always present and editable, blank ones included, since not
/// every board's BOM lists its fiducials. X/Y are the *untranslated* PnP coordinates (the board's
/// native design-file coordinates, before the board-origin offset shift applied to everything
/// else on import) for auto-detected entries - fiducials are physical reference points the
/// pick-and-place machine uses for its own alignment, so the raw coordinates are what will matter
/// later, not the shifted "world" coordinates used for on-screen rendering. Manually-added entries
/// have no such source of truth, so their X/Y start at 0 for the user to fill in by hand.
/// Every field is editable, including Designator, so the user can correct or fill in anything.
/// </summary>
public sealed class FiducialViewModel : ViewModelBase
{
    private string _designator;
    private double _x;
    private double _y;

    /// <summary>Which fiducial list (Top/BottomFiducials) this belongs to - needed so a
    /// screen-click "pick" (see MainViewModel.ConvertWorldPointToFiducialCoordinates) knows
    /// whether to apply the Bottom-side X mirror.</summary>
    public BoardSide Side { get; }

    public string Designator
    {
        get => _designator;
        set => SetField(ref _designator, value);
    }

    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public FiducialViewModel(string designator, double x, double y, BoardSide side)
    {
        _designator = designator;
        _x = x;
        _y = y;
        Side = side;
    }
}
