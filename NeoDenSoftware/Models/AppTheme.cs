namespace NeoDenSoftware.Models;

/// <summary>Which color theme is applied to the app's chrome (menus, toolbar, tabs, panels) - see
/// <see cref="Appearance.ThemeManager"/>. The PCB canvas itself is deliberately never themed - it
/// always renders the same way (dark background, soldermask-green board, etc.) regardless of this
/// setting, matching how every real PCB/CAD tool renders its board view the same way no matter
/// what color the surrounding app chrome is.</summary>
public enum AppTheme
{
    /// <summary>No theme applied - the default Windows look.</summary>
    Original,
    FabFloor,
    BenchLight,
    CopperFlux,
    VsDark,
}
