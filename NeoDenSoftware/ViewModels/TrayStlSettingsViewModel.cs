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

        if (ExtraLengthMm < 0 || ExtraWidthMm < 0 || GrooveWidthMm < 0 || GrooveDepthMm < 0 || TextDepthMm < 0)
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
            TextDepthMm, TextHeightMm));
        StatusMessage = "Saved.";
    }
}
