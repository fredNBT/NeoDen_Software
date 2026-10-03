using System.Windows.Media;
using NeoDenSoftware.Appearance;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Rendering;

/// <summary>
/// One shared, mutable, NEVER-frozen <see cref="SolidColorBrush"/> per <see cref="GerberLayerRole"/> -
/// every piece of geometry for a given layer/role (the Gerber-rendered Paths in
/// <see cref="GerberRenderer"/>, every placed component's footprint drawing on that side, and the
/// Layers panel's own color swatch) is built from the SAME brush object, not a copy. Changing a
/// layer's color is then just mutating that one brush's <see cref="SolidColorBrush.Color"/> -
/// WPF's Freezable change notification repaints every visual that references it immediately, with
/// no need to rebuild or re-import anything. Persisted via <see cref="LayerColorStore"/> so a
/// change survives an app restart.
/// </summary>
public static class LayerColors
{
    // Paste is deliberately absent here - per explicit instruction it always keeps its fixed
    // shiny-gold gradient look (built in MainViewModel.BuildShinyGoldBrush) and is never wired
    // into this shared-mutable-brush system at all, since a gradient has no single flat color a
    // user could pick to replace it with.
    private static readonly Dictionary<GerberLayerRole, SolidColorBrush> Brushes = new()
    {
        [GerberLayerRole.Outline] = new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x3C)),
        [GerberLayerRole.TopSoldermask] = new SolidColorBrush(Colors.White),
        [GerberLayerRole.BottomSoldermask] = new SolidColorBrush(Colors.White),
        [GerberLayerRole.TopComponents] = new SolidColorBrush(Colors.DeepSkyBlue),
        [GerberLayerRole.BottomComponents] = new SolidColorBrush(Colors.Orange),
    };

    static LayerColors()
    {
        foreach (var (role, (a, r, g, b)) in LayerColorStore.LoadAll())
            For(role).Color = Color.FromArgb(a, r, g, b);
    }

    /// <summary>The shared brush for this role - same instance every call, so callers can hold
    /// onto it (e.g. as a <c>Fill</c>/<c>Stroke</c>) and have it stay live across later color
    /// changes. A role with no built-in default (shouldn't normally happen) gets a fresh gray
    /// brush on first request rather than throwing.</summary>
    public static SolidColorBrush For(GerberLayerRole role)
    {
        if (!Brushes.TryGetValue(role, out var brush))
            Brushes[role] = brush = new SolidColorBrush(Colors.Gray);
        return brush;
    }

    /// <summary>Changes a role's color everywhere it's used and persists the change immediately -
    /// mirrors <see cref="Appearance.ThemeManager"/>'s "apply live + save, no separate Save step"
    /// pattern, since picking a color from a live swatch is exactly the kind of setting where
    /// instant feedback matters more than an explicit save.</summary>
    public static void SetColor(GerberLayerRole role, Color color)
    {
        For(role).Color = color;
        LayerColorStore.Save(role, color.A, color.R, color.G, color.B);
    }
}
