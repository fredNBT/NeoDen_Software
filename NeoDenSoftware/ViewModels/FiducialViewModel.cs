using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// One fiducial mark, either found in the BOM/PnP during import (designator or value containing
/// "FID" or "Fiducial") or added manually by the user via the Layers sidebar's "+ Add" button -
/// the sidebar's fiducial rows are always present and editable, blank ones included, since not
/// every board's BOM lists its fiducials. X/Y are the board's *translated/world* coordinates -
/// the same board-origin offset shift applied to every other placed component - for auto-detected
/// entries, same as a manually clicked pick: the gold on-screen mark has to line up with the
/// actual board feature (rendered in world coordinates), and the exported "mark" row has to sit in
/// the same coordinate space as every placement row around it. Manually-added entries have no such
/// source of truth, so their X/Y start at 0 for the user to fill in by hand.
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
        set
        {
            if (!SetField(ref _x, value)) return;
            OnPropertyChanged(nameof(TypedX));
            UpdateMarkPosition();
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (!SetField(ref _y, value)) return;
            OnPropertyChanged(nameof(TypedY));
            UpdateMarkPosition();
        }
    }

    /// <summary>What the sidebar's X/Y text boxes bind to: reads back the stored coordinate, but a
    /// value the user TYPES has the board's import offset added before it's stored - so typing a
    /// design-file coordinate lands at its real on-board position. Only the text boxes use these;
    /// clicked picks, PnP imports and project loads set <see cref="X"/>/<see cref="Y"/> directly,
    /// so no offset is ever applied to those a second time.</summary>
    public double TypedX
    {
        get => _x;
        set => X = value + _boardOffset().X;
    }

    public double TypedY
    {
        get => _y;
        set
        {
            var offset = _boardOffset();
            Y = (offset.NegateTypedY ? -Math.Abs(value) : value) + offset.Y;
        }
    }

    /// <summary>2mm gold circle drawn on the PCB (world/mm coordinates, hosted by the view) at
    /// this fiducial's stored position. Hidden until a position has been set.</summary>
    public Canvas Visual { get; } = new() { IsHitTestVisible = false };

    private readonly Ellipse _mark = new()
    {
        Width = MarkDiameterMm,
        Height = MarkDiameterMm,
        Fill = Brushes.Gold,
        IsHitTestVisible = false,
    };

    private readonly Func<(double X, double Y, bool NegateTypedY)> _boardOffset;

    public const double MarkDiameterMm = 2.0;

    public FiducialViewModel(string designator, double x, double y, BoardSide side, Func<(double X, double Y, bool NegateTypedY)>? boardOffset = null)
    {
        _designator = designator;
        _x = x;
        _y = y;
        Side = side;
        _boardOffset = boardOffset ?? (() => (0, 0, false));
        Visual.Children.Add(_mark);
        UpdateMarkPosition();
    }

    private void UpdateMarkPosition()
    {
        Canvas.SetLeft(_mark, _x - MarkDiameterMm / 2);
        Canvas.SetTop(_mark, _y - MarkDiameterMm / 2);
        _mark.Visibility = _x == 0 && _y == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
}
