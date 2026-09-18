using NeoDenSoftware.Appearance;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// Backs the "Appearance..." window (Settings menu): picks one of the app's color themes. Applies
/// and persists immediately on selection - a theme picker is the one settings window where
/// instant live feedback matters more than an explicit Save step, since the whole point is judging
/// how each option actually looks.
/// </summary>
public sealed class AppearanceSettingsViewModel : ViewModelBase
{
    private AppTheme _selectedTheme;

    private AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetField(ref _selectedTheme, value)) return;
            OnPropertyChanged(nameof(IsOriginal));
            OnPropertyChanged(nameof(IsFabFloor));
            OnPropertyChanged(nameof(IsBenchLight));
            OnPropertyChanged(nameof(IsCopperFlux));
        }
    }

    public bool IsOriginal
    {
        get => SelectedTheme == AppTheme.Original;
        set { if (value) Apply(AppTheme.Original); }
    }

    public bool IsFabFloor
    {
        get => SelectedTheme == AppTheme.FabFloor;
        set { if (value) Apply(AppTheme.FabFloor); }
    }

    public bool IsBenchLight
    {
        get => SelectedTheme == AppTheme.BenchLight;
        set { if (value) Apply(AppTheme.BenchLight); }
    }

    public bool IsCopperFlux
    {
        get => SelectedTheme == AppTheme.CopperFlux;
        set { if (value) Apply(AppTheme.CopperFlux); }
    }

    public AppearanceSettingsViewModel()
    {
        _selectedTheme = ThemeSettingsStore.Load();
    }

    private void Apply(AppTheme theme)
    {
        SelectedTheme = theme;
        ThemeManager.Apply(theme);
        ThemeSettingsStore.Save(theme);
    }
}
