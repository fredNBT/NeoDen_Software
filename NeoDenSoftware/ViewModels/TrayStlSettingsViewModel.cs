using System.Windows.Input;
using NeoDenSoftware.Models;
using NeoDenSoftware.Stl;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// Backs the "Tray STL Generator Settings..." window (Settings tab): the variables
/// <see cref="Stl.TrayStlGenerator"/> uses to build a tray-feeder parts tray STL, loaded from
/// <see cref="TrayStlSettingsStore"/> on construction and written back on Save.
/// </summary>
public sealed class TrayStlSettingsViewModel : ViewModelBase
{
    private double _boxLengthMm;
    private double _boxWidthMm;
    private double _boxHeightMm;
    private int _pocketCount;
    private double _extraLengthMm;
    private double _extraWidthMm;
    private double _grooveWidthMm;
    private double _grooveDepthMm;
    private double _textDepthMm;
    private double _textHeightMm;
    private double _componentCutoutSizeMm;
    private double _mountingHoleDiameterMm;
    private double _mountingHoleInsetMm;
    private double _pocketCenterHoleDiameterMm;
    private string? _statusMessage;

    public double BoxLengthMm
    {
        get => _boxLengthMm;
        set => SetField(ref _boxLengthMm, value);
    }

    public double BoxWidthMm
    {
        get => _boxWidthMm;
        set => SetField(ref _boxWidthMm, value);
    }

    public double BoxHeightMm
    {
        get => _boxHeightMm;
        set => SetField(ref _boxHeightMm, value);
    }

    public int PocketCount
    {
        get => _pocketCount;
        set => SetField(ref _pocketCount, value);
    }

    public double ExtraLengthMm
    {
        get => _extraLengthMm;
        set => SetField(ref _extraLengthMm, value);
    }

    public double ExtraWidthMm
    {
        get => _extraWidthMm;
        set => SetField(ref _extraWidthMm, value);
    }

    public double GrooveWidthMm
    {
        get => _grooveWidthMm;
        set => SetField(ref _grooveWidthMm, value);
    }

    public double GrooveDepthMm
    {
        get => _grooveDepthMm;
        set => SetField(ref _grooveDepthMm, value);
    }

    public double TextDepthMm
    {
        get => _textDepthMm;
        set => SetField(ref _textDepthMm, value);
    }

    /// <summary>Target physical glyph height in mm (a ceiling - shrunk further if the label
    /// doesn't fit the available space at this height; see <see cref="Stl.TrayStlGenerator"/>'s
    /// own doc comment).</summary>
    public double TextHeightMm
    {
        get => _textHeightMm;
        set => SetField(ref _textHeightMm, value);
    }

    /// <summary>Side length of the small square notch above and below each pocket, flush
    /// against (merged into) the pocket's own edge, same depth as the pocket - a fingernail catch
    /// reaching into the pocket to lift the part out. 0 disables it entirely.</summary>
    public double ComponentCutoutSizeMm
    {
        get => _componentCutoutSizeMm;
        set => SetField(ref _componentCutoutSizeMm, value);
    }

    /// <summary>Diameter of the two round mounting holes drilled all the way through the
    /// tray, one inset from the left edge and one from the right (see <see cref="MountingHoleInsetMm"/>),
    /// both vertically centered. 0 disables them entirely.</summary>
    public double MountingHoleDiameterMm
    {
        get => _mountingHoleDiameterMm;
        set => SetField(ref _mountingHoleDiameterMm, value);
    }

    /// <summary>Distance from each end of the tray to that hole's own center.</summary>
    public double MountingHoleInsetMm
    {
        get => _mountingHoleInsetMm;
        set => SetField(ref _mountingHoleInsetMm, value);
    }

    /// <summary>Diameter of the round hole through the middle of each pocket, straight through
    /// the whole tray. Skipped for a pocket individually (not for the whole tray) if it's wider
    /// than that pocket's own footprint-sized opening. 0 disables it entirely.</summary>
    public double PocketCenterHoleDiameterMm
    {
        get => _pocketCenterHoleDiameterMm;
        set => SetField(ref _pocketCenterHoleDiameterMm, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand SaveCommand { get; }

    public TrayStlSettingsViewModel()
    {
        var settings = TrayStlSettingsStore.Load();
        _boxLengthMm = settings.BoxLengthMm;
        _boxWidthMm = settings.BoxWidthMm;
        _boxHeightMm = settings.BoxHeightMm;
        _pocketCount = settings.PocketCount;
        _extraLengthMm = settings.ExtraLengthMm;
        _extraWidthMm = settings.ExtraWidthMm;
        _grooveWidthMm = settings.GrooveWidthMm;
        _grooveDepthMm = settings.GrooveDepthMm;
        _textDepthMm = settings.TextDepthMm;
        _textHeightMm = settings.TextHeightMm;
        _componentCutoutSizeMm = settings.ComponentCutoutSizeMm;
        _mountingHoleDiameterMm = settings.MountingHoleDiameterMm;
        _mountingHoleInsetMm = settings.MountingHoleInsetMm;
        _pocketCenterHoleDiameterMm = settings.PocketCenterHoleDiameterMm;

        SaveCommand = new RelayCommand(_ => Save());
    }

    private void Save()
    {
        if (PocketCount < 1)
        {
            StatusMessage = "Pockets per tray must be at least 1.";
            return;
        }

        if (BoxLengthMm <= 0 || BoxWidthMm <= 0 || BoxHeightMm <= 0)
        {
            StatusMessage = "Box length/width/height must all be greater than zero.";
            return;
        }

        if (ExtraLengthMm < 0 || ExtraWidthMm < 0 || GrooveWidthMm < 0 || GrooveDepthMm < 0 || TextDepthMm < 0 || ComponentCutoutSizeMm < 0 || MountingHoleDiameterMm < 0 || MountingHoleInsetMm < 0 || PocketCenterHoleDiameterMm < 0)
        {
            StatusMessage = "Clearance/groove/text values can't be negative.";
            return;
        }

        if (TextHeightMm <= 0)
        {
            StatusMessage = "Text height must be greater than zero.";
            return;
        }

        TrayStlSettingsStore.Save(new TrayStlSettings(
            BoxLengthMm, BoxWidthMm, BoxHeightMm, PocketCount, ExtraLengthMm, ExtraWidthMm, GrooveWidthMm, GrooveDepthMm,
            TextDepthMm, TextHeightMm, ComponentCutoutSizeMm, MountingHoleDiameterMm, MountingHoleInsetMm, PocketCenterHoleDiameterMm));
        StatusMessage = "Saved.";
    }
}
