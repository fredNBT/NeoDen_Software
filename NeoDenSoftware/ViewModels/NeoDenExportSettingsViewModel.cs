using NeoDenSoftware.Export;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

/// <summary>Editable NeoDen4 export values for the Machine Settings tab. Every change is saved
/// straight away (no Save button) and the exporter reads <see cref="Current"/> on each export.
/// Negative values are rejected (kept at the previous value) - a negative delay or base height
/// makes no sense for the machine.</summary>
public sealed class NeoDenExportSettingsViewModel : ViewModelBase
{
    private NeoDenExportSettings _current = NeoDenExportSettingsStore.Load();

    public NeoDenExportSettings Current => _current;

    public int PickDelayMs
    {
        get => _current.PickDelayMs;
        set => Update(value >= 0 && value != _current.PickDelayMs ? _current with { PickDelayMs = value } : null, nameof(PickDelayMs));
    }

    public int PlaceDelayMs
    {
        get => _current.PlaceDelayMs;
        set => Update(value >= 0 && value != _current.PlaceDelayMs ? _current with { PlaceDelayMs = value } : null, nameof(PlaceDelayMs));
    }

    public double TapePlaceHeightBaseMm
    {
        get => _current.TapePlaceHeightBaseMm;
        set => Update(value >= 0 && value != _current.TapePlaceHeightBaseMm ? _current with { TapePlaceHeightBaseMm = value } : null, nameof(TapePlaceHeightBaseMm));
    }

    private void Update(NeoDenExportSettings? changed, string property)
    {
        if (changed is not null)
        {
            _current = changed;
            NeoDenExportSettingsStore.Save(_current);
        }
        // Also raised for a rejected/unchanged entry so a text box showing it snaps back.
        OnPropertyChanged(property);
    }
}
