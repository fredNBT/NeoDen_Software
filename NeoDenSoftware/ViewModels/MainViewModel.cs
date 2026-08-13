using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using NeoDenSoftware.Feeders;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Gerber;
using NeoDenSoftware.Import;
using NeoDenSoftware.Models;
using NeoDenSoftware.Project;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private double _boardOriginX = 243.52;
    private double _boardOriginY = 37.58;

    // The board is repositioned on import so its bottom-left corner (min X, min Y) lands here,
    // in the same world-coordinate space the mouse position readout reports. Editable (Settings
    // menu > Board Origin...) rather than a fixed constant, but only takes effect on the *next*
    // import - it deliberately doesn't retroactively move a board already on screen, since that
    // would also require re-deriving every already-placed BOM component's position.
    public double BoardOriginX
    {
        get => _boardOriginX;
        set => SetField(ref _boardOriginX, value);
    }

    public double BoardOriginY
    {
        get => _boardOriginY;
        set => SetField(ref _boardOriginY, value);
    }

    /// <summary>World-space bounds of the always-present, non-import-tied fixtures. Exposed so
    /// the view can center the camera on them at startup, before any Gerber import has happened
    /// to otherwise establish an initial view (see <see cref="LayersImported"/>).</summary>
    public static readonly BoundingBox SteelRailFixtureBounds = new(82, -14, 215, 363);
    public static readonly BoundingBox SteelRailFixture2Bounds = new(237, -14, 372, 363);
    public static readonly BoundingBox LedRingBounds = new(382, 175, 419, 243);

    /// <summary>Union of every static fixture's bounds, including the (editable) tape feeder
    /// positions - use this (not the individual bounds above) wherever the camera needs to fit
    /// them, so adding a new fixture only means adding it here instead of also hunting down
    /// every camera-fit call site. Instance (not static) because the tape feeder positions can
    /// change at runtime via the Settings tab.</summary>
    public BoundingBox AllFixtureBounds
    {
        get
        {
            var bounds = SteelRailFixtureBounds.Include(SteelRailFixture2Bounds).Include(LedRingBounds);
            foreach (var feeder in TapeFeederSettings)
                bounds = bounds.Include(new BoundingBox(feeder.X, feeder.Y, feeder.X, feeder.Y));
            return bounds;
        }
    }

    private readonly ZipImportService _zipImportService = new();
    private readonly LayerClassifier _layerClassifier = new();
    private readonly GerberParser _gerberParser = new();
    private readonly BomImportService _bomImportService = new();
    private readonly PnpImportService _pnpImportService = new();

    private string? _statusMessage = "Import a Gerber zip to get started.";
    private double _pcbThicknessMm = 1.6;
    private bool _mirrorBottomLayers = true;
    private double _boardCenterX;
    private bool _hasBoardOffset;
    private double _boardOffsetX;
    private double _boardOffsetY;
    private Canvas? _topComponentsHost;
    private Canvas? _bottomComponentsHost;

    // Remembered so "Save Project" can write them out without re-prompting, and so a later
    // "Load Project" can re-run the exact same import(s) without asking the user to re-pick files
    // or re-answer the column mapping dialogs. Only ever set on a successful import (Gerber/BOM/PnP
    // each independently) - never cleared, so a Save right after any one of them still captures
    // whatever was last successfully imported.
    private string? _lastZipPath;
    private IReadOnlyDictionary<string, GerberLayerRole>? _lastLayerRoles;
    private string? _lastBomPath;
    private BomColumnMapping? _lastBomMapping;
    private string? _lastPnpPath;
    private PnpColumnMapping? _lastPnpMapping;

    private ComponentViewModel? _selectedComponent;
    private FiducialViewModel? _pendingFiducialPick;

    public ObservableCollection<LayerViewModel> Layers { get; } = [];
    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    /// <summary>BOM panel rows: one per (Value, FootprintText, Side) group across <see cref="Components"/>,
    /// rebuilt via <see cref="RebuildBomGroups"/> whenever that set changes (import, group removal).</summary>
    public ObservableCollection<BomGroupViewModel> BomGroups { get; } = [];

    /// <summary>Fiducial marks found during the last BOM/PnP import, split by side and shown in
    /// the Layers sidebar - see <see cref="FiducialViewModel"/> for why their X/Y are untranslated.</summary>
    public ObservableCollection<FiducialViewModel> TopFiducials { get; } = [];
    public ObservableCollection<FiducialViewModel> BottomFiducials { get; } = [];

    /// <summary>Editable tape feeder slot positions, shown on the Settings tab. Populated once at
    /// construction from <see cref="TapeFeederLibrary.Defaults"/>, with any saved
    /// <see cref="FeederPositionStore"/> override applied on top - so an edit from a previous
    /// session is restored instead of silently reset to the hardcoded default on every launch.
    /// Each entry's <c>Visual</c> is added to FixturesHost by the view, the same way the other
    /// static fixtures are.</summary>
    public ObservableCollection<TapeFeederSettingViewModel> TapeFeederSettings { get; } = new(BuildTapeFeederSettings());

    /// <summary>Editable tray feeder slot positions, shown on the Settings tab. Populated once at
    /// construction from <see cref="TrayFeederLibrary.Defaults"/>, with any saved
    /// <see cref="FeederPositionStore"/> override applied on top, same as tape feeders above -
    /// unlike tape feeders, trays have no physical tray-body Visual, just the assigned component
    /// image(s) drawn at their editable Begin/End positions.</summary>
    public ObservableCollection<TrayFeederSettingViewModel> TrayFeederSettings { get; } = new(BuildTrayFeederSettings());

    public ICommand ImportCommand { get; }
    public ICommand ImportComponentsCommand { get; }
    public ICommand RemoveComponentCommand { get; }
    public ICommand RemoveComponentGroupCommand { get; }
    public ICommand AutoAssignFeedersCommand { get; }
    public ICommand GenerateNeoDenCsvCommand { get; }
    public ICommand AddTopFiducialCommand { get; }
    public ICommand AddBottomFiducialCommand { get; }
    public ICommand RemoveFiducialCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand LoadProjectCommand { get; }

    /// <summary>The component last clicked on the canvas, if any - drives the info panel and
    /// what the spacebar rotates.</summary>
    public ComponentViewModel? SelectedComponent
    {
        get => _selectedComponent;
        set => SetField(ref _selectedComponent, value);
    }

    /// <summary>Non-null while waiting for the user to click the board to set this fiducial's
    /// X/Y ("pick mode", armed by Add{Top,Bottom}FiducialCommand) - the view watches this to
    /// show a crosshair cursor and to route the next canvas click into
    /// <see cref="ConvertWorldPointToFiducialCoordinates"/> instead of the normal pan/select
    /// behavior. Escape (handled by the view) cancels by clearing this back to null.</summary>
    public FiducialViewModel? PendingFiducialPick
    {
        get => _pendingFiducialPick;
        set => SetField(ref _pendingFiducialPick, value);
    }

    /// <summary>
    /// Set by the view to show the layer-role confirmation dialog. Returns the (possibly
    /// reassigned) file list to proceed with, or null if the user cancelled.
    /// </summary>
    public Func<IReadOnlyList<ExtractedFile>, IReadOnlyList<ExtractedFile>?>? ConfirmLayerAssignments { get; set; }

    /// <summary>Set by the view to prompt for a zip file to open. Returns null if cancelled.</summary>
    public Func<string?>? PromptForZipFile { get; set; }

    /// <summary>Set by the view to prompt for a BOM CSV file. Returns null if cancelled.</summary>
    public Func<string?>? PromptForBomFile { get; set; }

    /// <summary>Set by the view to prompt for a PnP/centroid CSV file. Returns null if cancelled.</summary>
    public Func<string?>? PromptForPnpFile { get; set; }

    /// <summary>Set by the view to show the BOM column-mapping dialog. Returns null if cancelled.</summary>
    public Func<IReadOnlyList<string>, BomColumnMapping, BomColumnMapping?>? ConfirmBomColumnMapping { get; set; }

    /// <summary>Set by the view to show the PnP column-mapping dialog. Returns null if cancelled.</summary>
    public Func<IReadOnlyList<string>, PnpColumnMapping, PnpColumnMapping?>? ConfirmPnpColumnMapping { get; set; }

    /// <summary>Set by the view to prompt for where to save the NeoDen4 CSV pair (a base path;
    /// "_Top"/"_Bottom" are appended before the extension). Returns null if cancelled.</summary>
    public Func<string?>? PromptForNeoDenSaveFile { get; set; }

    /// <summary>Set by the view to prompt for where to save a project (.pnp) file. Returns null
    /// if cancelled.</summary>
    public Func<string?>? PromptForSaveProjectFile { get; set; }

    /// <summary>Set by the view to prompt for which project (.pnp) file to load. Returns null if
    /// cancelled.</summary>
    public Func<string?>? PromptForLoadProjectFile { get; set; }

    public event EventHandler<BoundingBox>? LayersImported;

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    /// <summary>PCB thickness in mm, set alongside a Gerber import (defaults to the common 1.6mm)
    /// - not used by anything yet, captured for a later feature.</summary>
    public double PcbThicknessMm
    {
        get => _pcbThicknessMm;
        set => SetField(ref _pcbThicknessMm, value);
    }

    /// <summary>Mirrors bottom-side layers' X coordinates about the board's horizontal center, so
    /// they appear correctly when the board is physically flipped over to view the underside.</summary>
    public bool MirrorBottomLayers
    {
        get => _mirrorBottomLayers;
        set
        {
            if (SetField(ref _mirrorBottomLayers, value))
                ApplyBottomLayerMirroring();
        }
    }

    public MainViewModel()
    {
        ImportCommand = new AsyncRelayCommand(async _ => await ImportAsync());
        ImportComponentsCommand = new AsyncRelayCommand(async _ => await ImportComponentsAsync());
        RemoveComponentCommand = new RelayCommand(param =>
        {
            if (param is ComponentViewModel component)
                RemoveComponent(component);
        });
        RemoveComponentGroupCommand = new RelayCommand(param =>
        {
            if (param is BomGroupViewModel group)
                RemoveComponentGroup(group);
        });
        AutoAssignFeedersCommand = new RelayCommand(_ => AutoAssignFeeders());
        GenerateNeoDenCsvCommand = new RelayCommand(_ => GenerateNeoDenCsv(),
            _ => Components.Any(c => c.FeederNumber is not null));
        // Sidebar fiducial rows are always editable and always have a way to add more - not every
        // board's BOM lists its fiducials, so the user needs to be able to add them by hand rather
        // than only ever seeing whatever auto-detection found. Adding one also arms "pick mode" -
        // PendingFiducialPick - so the very next canvas click sets its X/Y instead of the user
        // having to type coordinates by hand.
        AddTopFiducialCommand = new RelayCommand(_ =>
        {
            var fiducial = new FiducialViewModel(string.Empty, 0, 0, BoardSide.Top);
            TopFiducials.Add(fiducial);
            PendingFiducialPick = fiducial;
        });
        AddBottomFiducialCommand = new RelayCommand(_ =>
        {
            var fiducial = new FiducialViewModel(string.Empty, 0, 0, BoardSide.Bottom);
            BottomFiducials.Add(fiducial);
            PendingFiducialPick = fiducial;
        });
        RemoveFiducialCommand = new RelayCommand(param =>
        {
            if (param is not FiducialViewModel fiducial) return;
            TopFiducials.Remove(fiducial);
            BottomFiducials.Remove(fiducial);
        });
        SaveProjectCommand = new RelayCommand(_ => SaveProject());
        LoadProjectCommand = new AsyncRelayCommand(async _ => await LoadProjectAsync());
    }

    /// <summary>Loads any saved tape feeder overrides once (not once per feeder), applying each on
    /// top of that feeder's hardcoded default - a feeder with no saved override just keeps using
    /// the default, so this works fine on a first run with no <see cref="FeederPositionStore"/>
    /// file yet.</summary>
    private static IEnumerable<TapeFeederSettingViewModel> BuildTapeFeederSettings()
    {
        var overrides = FeederPositionStore.LoadTapeOverrides();
        return TapeFeederLibrary.Defaults.Select(f =>
            new TapeFeederSettingViewModel(f.Number, overrides.GetValueOrDefault(f.Number, f.Position)));
    }

    private static IEnumerable<TrayFeederSettingViewModel> BuildTrayFeederSettings()
    {
        var overrides = FeederPositionStore.LoadTrayOverrides();
        return TrayFeederLibrary.Defaults.Select(p =>
            new TrayFeederSettingViewModel(overrides.GetValueOrDefault(p.FeederId, p)));
    }

    /// <summary>
    /// Builds the always-present, non-import-tied fixture visuals (steel rails, axis scale, LED
    /// ring) - permanent scene furniture with no visibility toggle, so unlike Gerber/BOM layers
    /// these are plain <see cref="UIElement"/>s, not <see cref="LayerViewModel"/>s. The view adds
    /// them directly to its own fixed FixturesHost canvas once at startup.
    /// </summary>
    public static IReadOnlyList<UIElement> BuildFixtureVisuals() =>
    [
        BuildSteelFixtureVisual(SteelRailFixtureBounds),
        BuildSteelFixtureVisual(SteelRailFixture2Bounds),
        BuildAxisScaleVisual(),
        BuildLedRingVisual(),
    ];

    private static UIElement BuildSteelFixtureVisual(BoundingBox bounds)
    {
        var rect = new Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Fill = BuildBrushedSteelBrush(),
            Stroke = Brushes.DimGray,
            StrokeThickness = 0.5,
        };
        Canvas.SetLeft(rect, bounds.MinX);
        Canvas.SetTop(rect, bounds.MinY);

        var host = new Canvas { IsHitTestVisible = false };
        host.Children.Add(rect);
        return host;
    }

    private static Brush BuildBrushedSteelBrush()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var rng = new Random(12345); // fixed seed so the streak pattern is stable across rebuilds
        const int stopCount = 24;

        for (var i = 0; i <= stopCount; i++)
        {
            var t = (double)i / stopCount;
            var sheen = 0.55 + 0.25 * Math.Sin(t * Math.PI); // darker at the edges, lighter in the middle
            var noise = (rng.NextDouble() - 0.5) * 0.12; // fine per-stop variation to fake brush streaks
            var value = Math.Clamp(sheen + noise, 0.25, 0.95);
            var gray = (byte)(value * 255);
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(gray, gray, gray), t));
        }

        gradient.Freeze();
        return gradient;
    }

    /// <summary>Diagonal metallic-highlight gradient for the paste layers - a flat solid gold
    /// reads flat/matte, so this fakes a specular sheen (bright near-white highlight band between
    /// two gold tones, darkening toward the far corner) the same way <see cref="BuildBrushedSteelBrush"/>
    /// fakes brushed steel. Applied per-layer (the paste layer's Path elements share one brush
    /// instance), so the highlight band falls consistently across the whole board diagonally
    /// rather than per individual pad.</summary>
    private static Brush BuildShinyGoldBrush()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xB8, 0x86, 0x0B), 0.0));  // dark goldenrod - depth
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD7, 0x00), 0.3));  // rich gold
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xF0), 0.5));  // near-white specular highlight
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD7, 0x00), 0.7));  // rich gold
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0xB8, 0x86, 0x0B), 1.0));  // dark goldenrod - depth
        gradient.Freeze();
        return gradient;
    }

    private static UIElement BuildAxisScaleVisual()
    {
        const double axisLengthMm = 400;
        const double minorTickMm = 10;
        const double majorTickMm = 50;
        const double tickLengthMm = 3;
        const double labelSizeMm = 4;

        var axisBrush = Brushes.LightGray;
        var host = new Canvas { IsHitTestVisible = false };

        host.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = axisLengthMm, Y2 = 0, Stroke = axisBrush, StrokeThickness = 0.3 });
        host.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = 0, Y2 = axisLengthMm, Stroke = axisBrush, StrokeThickness = 0.3 });

        for (var x = 0.0; x <= axisLengthMm; x += minorTickMm)
        {
            var isMajor = x % majorTickMm == 0;
            host.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = isMajor ? -tickLengthMm * 2 : -tickLengthMm,
                Stroke = axisBrush, StrokeThickness = 0.2,
            });

            if (isMajor && x > 0)
                AddLabel(host, x, -tickLengthMm * 2 - labelSizeMm, x.ToString("0"), labelSizeMm, axisBrush);
        }

        for (var y = 0.0; y <= axisLengthMm; y += minorTickMm)
        {
            var isMajor = y % majorTickMm == 0;
            host.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = isMajor ? -tickLengthMm * 2 : -tickLengthMm, Y2 = y,
                Stroke = axisBrush, StrokeThickness = 0.2,
            });

            if (isMajor && y > 0)
                AddLabel(host, -tickLengthMm * 2 - labelSizeMm * 2, y - labelSizeMm / 2, y.ToString("0"), labelSizeMm, axisBrush);
        }

        return host;
    }

    private static void AddLabel(Canvas host, double x, double y, string text, double fontSizeMm, Brush brush)
    {
        var label = new TextBlock { Text = text, FontSize = fontSizeMm, Foreground = brush };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        // Labels are drawn in the same world space as everything else, which is Y-up; text
        // itself renders top-down internally, so flip it back upright under the shared Y-flip.
        label.RenderTransform = new ScaleTransform(1, -1);
        host.Children.Add(label);
    }

    private static UIElement BuildLedRingVisual()
    {
        const double targetSpacingMm = 10.0;

        var bounds = LedRingBounds;
        var corners = new[]
        {
            new Point(bounds.MinX, bounds.MinY),
            new Point(bounds.MaxX, bounds.MinY),
            new Point(bounds.MaxX, bounds.MaxY),
            new Point(bounds.MinX, bounds.MaxY),
        };

        var edgeLengths = new double[4];
        var perimeter = 0.0;
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            edgeLengths[i] = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
            perimeter += edgeLengths[i];
        }

        var ledCount = Math.Max(4, (int)Math.Round(perimeter / targetSpacingMm));
        var spacing = perimeter / ledCount;

        var ledFootprint = FootprintLibrary.All.First(f => f.Name == "0805");
        var glowColor = Color.FromRgb(0xFF, 0xF4, 0xA3); // warm yellow/white
        var ledColor = Freeze(new SolidColorBrush(glowColor));
        var haloBrush = Freeze(BuildHaloBrush(glowColor));

        var host = new Canvas { IsHitTestVisible = false };

        for (var i = 0; i < ledCount; i++)
        {
            var target = i * spacing;
            var cumulative = 0.0;
            var edgeIndex = 0;
            while (edgeIndex < 3 && cumulative + edgeLengths[edgeIndex] < target)
            {
                cumulative += edgeLengths[edgeIndex];
                edgeIndex++;
            }

            var a = corners[edgeIndex];
            var b = corners[(edgeIndex + 1) % 4];
            var t = edgeLengths[edgeIndex] > 0 ? (target - cumulative) / edgeLengths[edgeIndex] : 0;
            var x = a.X + (b.X - a.X) * t;
            var y = a.Y + (b.Y - a.Y) * t;
            var angleDegrees = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;

            host.Children.Add(BuildGlowingLed(ledFootprint, ledColor, haloBrush, x, y, angleDegrees));
        }

        return host;
    }

    private static UIElement BuildGlowingLed(FootprintDefinition led, Brush color, Brush haloBrush, double x, double y, double angleDegrees)
    {
        // A single DropShadowEffect on the body alone reads as a dim rim-light; layering a
        // separate, larger radial-gradient halo underneath gives a much brighter, more
        // convincing bloom, with the (still-strengthened) DropShadowEffect adding a tight
        // bright edge right at the LED body itself.
        var haloSize = led.LengthMm * 6;
        var halo = new Ellipse { Width = haloSize, Height = haloSize, Fill = haloBrush };
        Canvas.SetLeft(halo, -haloSize / 2);
        Canvas.SetTop(halo, -haloSize / 2);

        var body = FootprintRenderer.BuildComponentVisual(led, color);
        body.Effect = new DropShadowEffect
        {
            Color = ((SolidColorBrush)color).Color,
            BlurRadius = led.LengthMm * 5,
            ShadowDepth = 0,
            Opacity = 1.0,
        };

        var wrapper = new Canvas
        {
            RenderTransform = new TransformGroup
            {
                Children = { new RotateTransform(angleDegrees), new TranslateTransform(x, y) },
            },
        };
        wrapper.Children.Add(halo);
        wrapper.Children.Add(body);

        return wrapper;
    }

    private static Brush BuildHaloBrush(Color glowColor)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(200, glowColor.R, glowColor.G, glowColor.B), 0.0),
                new GradientStop(Color.FromArgb(80, glowColor.R, glowColor.G, glowColor.B), 0.5),
                new GradientStop(Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B), 1.0),
            },
        };
        return brush;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// <paramref name="zipPathOverride"/> is set by <see cref="LoadProjectAsync"/> to re-import
    /// the exact file a saved project points at, without prompting the user to pick it again.
    /// <paramref name="layerRoleOverrides"/> (also only set by a project load) carries the roles
    /// the user actually confirmed in a previous session, keyed by filename - applied on top of
    /// fresh auto-detection and skipping the confirmation dialog entirely, so a manual correction
    /// (e.g. a file auto-detected as something other than Outline, then corrected by hand) still
    /// round-trips through a save/load instead of silently reverting to the auto-detected guess.
    /// </summary>
    private async Task ImportAsync(string? zipPathOverride = null, IReadOnlyDictionary<string, GerberLayerRole>? layerRoleOverrides = null)
    {
        var zipPath = zipPathOverride ?? PromptForZipFile?.Invoke();
        if (string.IsNullOrEmpty(zipPath)) return;

        StatusMessage = "Extracting zip...";
        List<ExtractedFile> extracted;
        try
        {
            extracted = await Task.Run(() => _zipImportService.ExtractAndList(zipPath));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to extract zip: {ex.Message}";
            return;
        }

        await Task.Run(() => _layerClassifier.ClassifyAll(extracted));

        IReadOnlyList<ExtractedFile>? confirmed;
        if (layerRoleOverrides is not null)
        {
            foreach (var file in extracted)
            {
                if (layerRoleOverrides.TryGetValue(file.FileName, out var role))
                    file.DetectedRole = role;
            }
            confirmed = extracted;
        }
        else
        {
            confirmed = ConfirmLayerAssignments?.Invoke(extracted) ?? extracted;
        }

        if (confirmed is null)
        {
            StatusMessage = "Import cancelled.";
            return;
        }

        var roleFiles = confirmed
            .Where(f => f.DetectedRole is GerberLayerRole.TopPaste or GerberLayerRole.BottomPaste
                or GerberLayerRole.TopSoldermask or GerberLayerRole.BottomSoldermask or GerberLayerRole.Outline)
            .ToList();

        if (roleFiles.Count == 0)
        {
            StatusMessage = "No files were assigned to a layer.";
            return;
        }

        StatusMessage = "Parsing Gerber files...";

        List<(GerberLayerRole Role, ParsedLayer Layer)> parsedLayers;
        try
        {
            parsedLayers = await Task.Run(() => roleFiles
                .Select(f => (f.DetectedRole, Layer: _gerberParser.Parse(f.FullPath, f.DetectedRole)))
                // Outline first so its filled board shape sits behind paste/soldermask/components
                // rather than being drawn on top of them (stable sort keeps the rest in place).
                .OrderBy(t => t.DetectedRole == GerberLayerRole.Outline ? 0 : 1)
                .ToList());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to parse Gerber files: {ex.Message}";
            return;
        }

        var rawBounds = BoundingBox.Empty;
        foreach (var (_, layer) in parsedLayers)
        {
            if (layer.Bounds.MaxX >= layer.Bounds.MinX)
                rawBounds = rawBounds.Include(layer.Bounds);
        }

        var hasGeometry = rawBounds.MaxX >= rawBounds.MinX;
        var dx = hasGeometry ? BoardOriginX - rawBounds.MinX : 0;
        var dy = hasGeometry ? BoardOriginY - rawBounds.MinY : 0;

        // Only drop the previous Gerber-derived layers - the fixture and any imported BOM
        // components layers aren't tied to this import and should survive a re-import.
        foreach (var stale in Layers.Where(l => l.Role is GerberLayerRole.TopPaste or GerberLayerRole.BottomPaste
                     or GerberLayerRole.TopSoldermask or GerberLayerRole.BottomSoldermask or GerberLayerRole.Outline).ToList())
            Layers.Remove(stale);

        var combinedBounds = BoundingBox.Empty;

        foreach (var (role, layer) in parsedLayers)
        {
            var shiftedLayer = Translate(layer, dx, dy);
            var visual = GerberRenderer.BuildLayerElement(shiftedLayer, DefaultColorFor(role));
            Layers.Add(new LayerViewModel
            {
                Name = DefaultNameFor(role),
                Role = role,
                Color = DefaultColorFor(role),
                Visual = visual,
            });

            if (shiftedLayer.Bounds.MaxX >= shiftedLayer.Bounds.MinX)
                combinedBounds = combinedBounds.Include(shiftedLayer.Bounds);
        }

        var finalBounds = combinedBounds.MaxX >= combinedBounds.MinX
            ? combinedBounds
            : new BoundingBox(BoardOriginX, BoardOriginY, BoardOriginX + 100, BoardOriginY + 100);

        _boardCenterX = (finalBounds.MinX + finalBounds.MaxX) / 2;
        _boardOffsetX = dx;
        _boardOffsetY = dy;
        _hasBoardOffset = true;
        _lastZipPath = zipPath;
        // Every file's FINAL role (post-dialog, or post-override on a project reload) - not just
        // the ones that ended up as a recognized paste/soldermask/outline layer - so "Save
        // Project" can faithfully reproduce whatever the user actually confirmed, including any
        // file left as Unknown on purpose. Built with a plain loop (not ToDictionary) so a
        // duplicate filename in an unusual zip overwrites rather than throwing.
        var lastLayerRoles = new Dictionary<string, GerberLayerRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in confirmed)
            lastLayerRoles[file.FileName] = file.DetectedRole;
        _lastLayerRoles = lastLayerRoles;
        ApplyBottomLayerMirroring();

        StatusMessage = $"Loaded {Layers.Count} layer(s).";
        // The initial view should show the board AND the always-present steel rail fixtures -
        // otherwise a fixture (positioned well away from wherever the board lands) falls outside
        // the auto-fit camera the moment a board is imported. _boardCenterX above is
        // deliberately NOT widened by this, since the bottom-layer mirror axis must stay based
        // on the board alone.
        LayersImported?.Invoke(this, finalBounds.Include(AllFixtureBounds));
    }

    /// <summary>
    /// The four *Override parameters are set by <see cref="LoadProjectAsync"/> to re-import the
    /// exact files (and re-apply the exact confirmed column mappings) a saved project points at,
    /// without prompting the user to pick files or re-answer the mapping dialogs. <c>ReadHeader</c>
    /// is still called either way even when a mapping override is given - it's also where the
    /// PnP file's own <c>unitToMm</c> gets parsed from its preamble text, which isn't part of a
    /// user's column mapping and so isn't something a saved project needs to remember separately.
    /// </summary>
    private async Task ImportComponentsAsync(string? bomPathOverride = null, BomColumnMapping? bomMappingOverride = null,
        string? pnpPathOverride = null, PnpColumnMapping? pnpMappingOverride = null)
    {
        if (!_hasBoardOffset)
        {
            StatusMessage = "Import a Gerber zip first, so components line up with the same board coordinates.";
            return;
        }

        var bomPath = bomPathOverride ?? PromptForBomFile?.Invoke();
        if (string.IsNullOrEmpty(bomPath)) return;

        var (bomColumns, bomGuess) = await Task.Run(() => _bomImportService.ReadHeader(bomPath));
        if (bomColumns.Count == 0)
        {
            StatusMessage = "Could not read any columns from the BOM file.";
            return;
        }

        var bomMapping = bomMappingOverride ?? ConfirmBomColumnMapping?.Invoke(bomColumns, bomGuess);
        if (bomMapping is null)
        {
            StatusMessage = "Import cancelled.";
            return;
        }

        var pnpPath = pnpPathOverride ?? PromptForPnpFile?.Invoke();
        if (string.IsNullOrEmpty(pnpPath)) return;

        var (pnpColumns, pnpGuess, unitToMm) = await Task.Run(() => _pnpImportService.ReadHeader(pnpPath));
        if (pnpColumns.Count == 0)
        {
            StatusMessage = "Could not read any columns from the PnP file.";
            return;
        }

        var pnpMapping = pnpMappingOverride ?? ConfirmPnpColumnMapping?.Invoke(pnpColumns, pnpGuess);
        if (pnpMapping is null)
        {
            StatusMessage = "Import cancelled.";
            return;
        }

        _lastBomPath = bomPath;
        _lastBomMapping = bomMapping;
        _lastPnpPath = pnpPath;
        _lastPnpMapping = pnpMapping;

        StatusMessage = "Parsing BOM and PnP files...";

        List<BomEntry> bom;
        List<PnpEntry> placements;
        try
        {
            bom = await Task.Run(() => _bomImportService.Parse(bomPath, bomMapping));
            placements = await Task.Run(() => _pnpImportService.Parse(pnpPath, pnpMapping, unitToMm));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to parse BOM/PnP files: {ex.Message}";
            return;
        }

        var placementByDesignator = placements
            .GroupBy(p => p.Designator, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var stale in Layers.Where(l => l.Role is GerberLayerRole.TopComponents or GerberLayerRole.BottomComponents).ToList())
            Layers.Remove(stale);
        Components.Clear();
        TopFiducials.Clear();
        BottomFiducials.Clear();
        SelectedComponent = null;

        _topComponentsHost = new Canvas { IsHitTestVisible = true };
        _bottomComponentsHost = new Canvas { IsHitTestVisible = true };

        foreach (var bomEntry in bom)
        {
            var hasPlacement = placementByDesignator.TryGetValue(bomEntry.Designator, out var placement);
            var side = hasPlacement ? placement!.Side : BoardSide.Top;
            var initialFootprint = FootprintMatcher.Match(bomEntry.Footprint) ?? FootprintLibrary.NoMatch;

            var xMm = hasPlacement ? placement!.XMm + _boardOffsetX : 0;
            var yMm = hasPlacement ? placement!.YMm + _boardOffsetY : 0;
            var rotationDegrees = hasPlacement ? placement!.RotationDegrees : 0;

            Canvas? visual = null;
            if (hasPlacement)
            {
                visual = new Canvas { IsHitTestVisible = true };
                (side == BoardSide.Top ? _topComponentsHost : _bottomComponentsHost).Children.Add(visual);
            }

            var component = new ComponentViewModel(bomEntry.Designator, bomEntry.Value, bomEntry.Footprint, side, hasPlacement, xMm, yMm, rotationDegrees, initialFootprint, visual);
            if (visual is not null) visual.Tag = component;

            Components.Add(component);

            // Fiducials get their own sidebar entry, in *untranslated* PnP coordinates (no
            // _boardOffsetX/Y applied) - they're physical machine-alignment reference points, so
            // the raw design-file coordinates are what will matter later, not the shifted
            // on-screen "world" coordinates every other placed component uses. Bottom-side
            // fiducials get their X mirrored about the board's own (untranslated) horizontal
            // center - same "invert X on bottom" convention as everywhere else in this app
            // (the MirrorBottomLayers checkbox), since a bottom fiducial's raw PnP X is measured
            // from the top-view design file, not from the flipped view the machine actually sees
            // it in. _boardCenterX is in translated/world space, so subtracting _boardOffsetX
            // gets back to the matching untranslated center before reflecting.
            if (hasPlacement && IsFiducial(bomEntry.Designator, bomEntry.Value))
            {
                var fiducial = new FiducialViewModel(bomEntry.Designator, MirrorXIfBottom(placement!.XMm, side), placement.YMm, side);
                (side == BoardSide.Top ? TopFiducials : BottomFiducials).Add(fiducial);
            }
        }

        if (_topComponentsHost.Children.Count > 0)
        {
            Layers.Add(new LayerViewModel
            {
                Name = "Top Components",
                Role = GerberLayerRole.TopComponents,
                Color = Brushes.DeepSkyBlue,
                Visual = _topComponentsHost,
            });
        }

        if (_bottomComponentsHost.Children.Count > 0)
        {
            Layers.Add(new LayerViewModel
            {
                Name = "Bottom Components",
                Role = GerberLayerRole.BottomComponents,
                Color = Brushes.Orange,
                Visual = _bottomComponentsHost,
            });
        }

        ApplyBottomLayerMirroring();
        RebuildBomGroups();

        var placedCount = Components.Count(c => c.HasPlacement);
        StatusMessage = $"Loaded {Components.Count} BOM item(s), {placedCount} placed on the board.";
    }

    /// <summary>Writes every editable piece of session state - the original source file paths and
    /// confirmed column mappings, plus every edit layered on top (footprint changes, rotations,
    /// feeder assignments, removed BOM rows, fiducials) - out to a single ".pnp" project file, per
    /// explicit request. Requires a Gerber import to have happened at least once this session
    /// (<see cref="_lastZipPath"/> unset otherwise) - there'd be nothing meaningful to reload
    /// without it.</summary>
    private void SaveProject()
    {
        if (_lastZipPath is null)
        {
            StatusMessage = "Import a Gerber zip before saving a project - there's nothing to save yet.";
            return;
        }

        var path = PromptForSaveProjectFile?.Invoke();
        if (string.IsNullOrEmpty(path)) return;

        var data = new ProjectData(
            _lastZipPath, _lastBomPath, _lastPnpPath,
            BoardOriginX, BoardOriginY, PcbThicknessMm, MirrorBottomLayers,
            _lastBomMapping, _lastPnpMapping,
            Components.Select(c => new ProjectComponentRow(c.Designator, c.SelectedFootprint.Name, c.RotationDegrees, c.FeederNumber, c.UseHighFeederBank)).ToList(),
            TopFiducials.Select(f => new ProjectFiducialRow(BoardSide.Top, f.Designator, f.X, f.Y))
                .Concat(BottomFiducials.Select(f => new ProjectFiducialRow(BoardSide.Bottom, f.Designator, f.X, f.Y)))
                .ToList(),
            (_lastLayerRoles ?? new Dictionary<string, GerberLayerRole>())
                .Select(kv => new ProjectLayerRoleRow(kv.Key, kv.Value)).ToList());

        try
        {
            ProjectFileService.Save(path, data);
            StatusMessage = $"Saved project to {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save project: {ex.Message}";
        }
    }

    /// <summary>Re-imports the Gerber/BOM/PnP files a saved project points at (no re-prompting -
    /// see the *Override parameters on <see cref="ImportAsync"/>/<see cref="ImportComponentsAsync"/>,
    /// including the per-file layer-role assignments the user originally confirmed), then
    /// re-applies every saved edit on top: drops any component the saved list doesn't mention
    /// (the user had removed it before saving), restores each surviving component's
    /// footprint/rotation/feeder assignment, and replaces the freshly-detected fiducials with the
    /// exact saved set (which may include manual additions/edits a fresh detection wouldn't
    /// reproduce).</summary>
    private async Task LoadProjectAsync()
    {
        var path = PromptForLoadProjectFile?.Invoke();
        if (string.IsNullOrEmpty(path)) return;

        ProjectData data;
        try
        {
            data = ProjectFileService.Load(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to read project file: {ex.Message}";
            return;
        }

        if (string.IsNullOrEmpty(data.GerberZipPath) || !File.Exists(data.GerberZipPath))
        {
            StatusMessage = $"Project's Gerber zip not found: {data.GerberZipPath}";
            return;
        }

        BoardOriginX = data.BoardOriginX;
        BoardOriginY = data.BoardOriginY;
        PcbThicknessMm = data.PcbThicknessMm;
        MirrorBottomLayers = data.MirrorBottomLayers;

        var layerRoleOverrides = data.LayerRoles.ToDictionary(r => r.FileName, r => r.Role, StringComparer.OrdinalIgnoreCase);
        await ImportAsync(data.GerberZipPath, layerRoleOverrides);
        if (!_hasBoardOffset)
        {
            StatusMessage = "Failed to reload the project's Gerber board.";
            return;
        }

        if (!string.IsNullOrEmpty(data.BomPath) && File.Exists(data.BomPath) &&
            !string.IsNullOrEmpty(data.PnpPath) && File.Exists(data.PnpPath))
        {
            await ImportComponentsAsync(data.BomPath, data.BomMapping, data.PnpPath, data.PnpMapping);
        }

        var savedByDesignator = data.Components.ToDictionary(c => c.Designator, StringComparer.OrdinalIgnoreCase);
        foreach (var component in Components.ToList())
        {
            if (!savedByDesignator.TryGetValue(component.Designator, out var saved))
            {
                // Not in the saved project's component list - the user had removed it before saving.
                RemoveComponent(component);
                continue;
            }

            var footprint = FootprintLibrary.AllWithNoMatch.FirstOrDefault(f => f.Name == saved.FootprintName);
            if (footprint is not null) component.SelectedFootprint = footprint;
            component.RotationDegrees = saved.RotationDegrees;
            component.FeederNumber = saved.FeederNumber;
            component.UseHighFeederBank = saved.UseHighFeederBank;
        }

        // Replace whatever the BOM/PnP re-import auto-detected with the exact saved fiducial set.
        TopFiducials.Clear();
        BottomFiducials.Clear();
        foreach (var f in data.Fiducials)
        {
            var fiducial = new FiducialViewModel(f.Designator, f.X, f.Y, f.Side);
            (f.Side == BoardSide.Bottom ? BottomFiducials : TopFiducials).Add(fiducial);
        }

        RefreshFeederVisuals();
        RebuildBomGroups();

        StatusMessage = $"Loaded project from {System.IO.Path.GetFileName(path)}.";
    }

    /// <summary>Redraws each tape/tray feeder's assigned-component image/label from whatever
    /// FeederNumber/UseHighFeederBank the Components currently carry, WITHOUT reassigning any
    /// numbers - unlike <see cref="AutoAssignFeeders"/>, which both assigns numbers and draws
    /// visuals in one pass. Needed after <see cref="LoadProjectAsync"/> restores feeder
    /// assignments that were made in a previous session, so the Board View's feeder visuals catch
    /// up to match instead of staying blank until the next Auto-Assign run.</summary>
    private void RefreshFeederVisuals()
    {
        foreach (var feeder in TapeFeederSettings) feeder.ClearAssignedComponent();
        foreach (var tray in TrayFeederSettings) tray.ClearAssignedComponent();

        foreach (var group in Components.Where(c => c.FeederNumber is not null).GroupBy(c => c.FeederNumber!.Value))
        {
            var representative = group.First();
            if (representative.UseHighFeederBank)
            {
                var tray = TrayFeederSettings.FirstOrDefault(f => f.FeederId == group.Key);
                tray?.SetAssignedComponent(representative.Value, representative.SelectedFootprint);
            }
            else
            {
                var feeder = TapeFeederSettings.FirstOrDefault(f => f.Number == group.Key.ToString());
                feeder?.SetAssignedComponent($"{representative.Value}\n{representative.FootprintText}", representative.SelectedFootprint);
            }
        }
    }

    /// <summary>Rebuilds the BOM panel's grouped view from scratch - cheap enough at BOM sizes
    /// this app deals with to just do a full Clear+repopulate rather than track incremental
    /// membership changes.</summary>
    private void RebuildBomGroups()
    {
        foreach (var old in BomGroups)
            old.Detach();
        BomGroups.Clear();
        foreach (var group in Components.GroupBy(c => (Value: c.Value ?? "", FootprintText: c.FootprintText ?? "", c.Side)))
            BomGroups.Add(new BomGroupViewModel(group.ToList()));
    }

    private void RemoveComponentGroup(BomGroupViewModel group)
    {
        foreach (var member in group.Members.ToList())
            RemoveComponent(member);
        RebuildBomGroups();
    }

    private void RemoveComponent(ComponentViewModel component)
    {
        if (component.Visual is not null)
        {
            var host = component.Side == BoardSide.Top ? _topComponentsHost : _bottomComponentsHost;
            host?.Children.Remove(component.Visual);
        }

        Components.Remove(component);
        if (ReferenceEquals(SelectedComponent, component))
            SelectedComponent = null;

        CommandManager.InvalidateRequerySuggested();
    }

    public void RotateSelectedComponent(double degrees)
    {
        if (SelectedComponent is { HasPlacement: true } component)
            component.RotationDegrees += degrees;
    }

    /// <summary>Header row for the NeoDen4 "stack" (feeder table) section - copied verbatim from
    /// the user's real NeoDenTemplate export, including its trailing unnamed/empty columns, so
    /// the generated file matches the exact 33-column layout the machine software expects.</summary>
    private const string NeoDenStackHeader =
        "#Feeder,Feeder ID,Type,Nozzle,X,Y,Angle,Footprint,Value,Pick height,Pick delay,Place Height,Place Delay," +
        "Vacuum detection,Threshold,Vision Alignment,Speed,,,,,,,,,,,,,,,,";

    /// <summary>Fixed rows following the "mark" row, in the exact order/field-width (33 columns
    /// each, matching the stack rows) the user's real NeoDenTemplate export uses - none of these
    /// have their own header row, unlike "stack"/"comp". Copied verbatim. The "pcb" row (which
    /// precedes "mark" in the real template) is deliberately not included yet - not asked for.</summary>
    private static readonly string[] NeoDenAfterMarkBoilerplateLines =
    [
        "markext,0,0.8,3,1,0,,,,,,,,,,,,,,,,,,,,,,,,,,,",
        "markext,1,0.8,3,1,0,,,,,,,,,,,,,,,,,,,,,,,,,,,",
        "test,No,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,",
    ];

    /// <summary>Header row for the "comp" (placement) section - copied verbatim from the real
    /// template, same 33-column width as everything else.</summary>
    private const string NeoDenSmdHeader =
        "#SMD,Feeder ID,Nozzle,Name,Value,Footprint,X,Y,Rotation,Skip,,,,,,,,,,,,,,,,,,,,,,,";

    /// <summary>Builds the "mark" row's 4 numeric fields from the first two fiducials on this
    /// side (X1, Y1, X2, Y2) - not actually boilerplate, per the user's correction: these are the
    /// board's own fiducial coordinates, one pair per fiducial, not a fixed default. Missing
    /// fiducials (fewer than 2 defined on this side) fill in as 0, per explicit instruction.</summary>
    private static string BuildMarkLine(IReadOnlyList<FiducialViewModel> fiducials)
    {
        var x1 = fiducials.Count > 0 ? fiducials[0].X : 0;
        var y1 = fiducials.Count > 0 ? fiducials[0].Y : 0;
        var x2 = fiducials.Count > 1 ? fiducials[1].X : 0;
        var y2 = fiducials.Count > 1 ? fiducials[1].Y : 0;

        // 7 real fields + 26 trailing empties = 33 total, matching the real template's "mark" row
        // width exactly (verified via `awk -F',' '{print NF}'` against the actual template file,
        // not hand-counted - see the lesson from getting this wrong once already this round).
        var fields = new List<string> { "mark", "Whole", "Auto", x1.ToString("0.####"), y1.ToString("0.####"), x2.ToString("0.####"), y2.ToString("0.####") };
        fields.AddRange(Enumerable.Repeat(string.Empty, 26));
        return string.Join(",", fields.Select(EscapeNeoDenCsvField));
    }

    /// <summary>"mirror_create" row - (firstX, firstY) are the first placed component's own
    /// (offset-translated) position on this side, per explicit instruction; everything else is a
    /// fixed literal copied from the real template. 10 real fields + 23 empty = 33.</summary>
    private static string BuildMirrorCreateLine(double firstX, double firstY)
    {
        var fields = new List<string>
        {
            "mirror_create", "1", "1",
            firstX.ToString("0.####"), firstY.ToString("0.####"),
            "0", "0", "339.7", "134.57", "0.170895",
        };
        fields.AddRange(Enumerable.Repeat(string.Empty, 23));
        return string.Join(",", fields.Select(EscapeNeoDenCsvField));
    }

    /// <summary>"mirror" row - same (firstX, firstY) as <see cref="BuildMirrorCreateLine"/>.
    /// 5 real fields + 28 empty = 33.</summary>
    private static string BuildMirrorLine(double firstX, double firstY)
    {
        var fields = new List<string> { "mirror", firstX.ToString("0.####"), firstY.ToString("0.####"), "0", "No" };
        fields.AddRange(Enumerable.Repeat(string.Empty, 28));
        return string.Join(",", fields.Select(EscapeNeoDenCsvField));
    }

    /// <summary>One "comp" row per placed component on this side - unlike "stack" (one row per
    /// distinct feeder), this is one row per individual component, matching the real template's
    /// own comp section. Footprint is left blank - every comp row in the real template leaves it
    /// blank too (footprint only matters for the stack/feeder section, not placement), matching
    /// the user's own example row despite their field description otherwise suggesting it should
    /// be filled in - the empirical evidence (every real row, and their own example) won out.
    /// X is mirrored for a Bottom-side component when <see cref="MirrorBottomLayers"/> is checked
    /// - the same "invert X on bottom" the checkbox already applies to on-screen Bottom-role
    /// layer visuals, now also applied to the exported position so the file matches what's shown
    /// mirrored on screen. 10 real fields + 23 empty = 33.</summary>
    private string BuildCompRow(ComponentViewModel component)
    {
        var x = MirrorXIfBottomLayersChecked(component.XMm, component.Side);
        var fields = new List<string>
        {
            "comp",
            component.FeederNumber?.ToString() ?? "",
            "1", // Nozzle - always 1, per explicit instruction (the real template varies this, but the user overrode it)
            component.Designator,
            component.Value ?? "",
            "", // Footprint - see summary above
            x.ToString("0.####"), // offset-translated (world/PnP-plus-offset), then Bottom-mirrored if the checkbox is on
            component.YMm.ToString("0.####"),
            component.RotationDegrees.ToString("0.####"), // reflects any spacebar rotation from the UI
            "No", // Skip - always No, per explicit instruction
        };
        fields.AddRange(Enumerable.Repeat(string.Empty, 23));
        return string.Join(",", fields.Select(EscapeNeoDenCsvField));
    }

    /// <summary>Mirrors a Bottom-side X about the board's own (translated/world) center when
    /// <see cref="MirrorBottomLayers"/> is checked - matches the exact transform
    /// <see cref="ApplyBottomLayerMirroring"/> already applies to Bottom-role layer visuals on
    /// screen (<c>ScaleTransform(-1,1,_boardCenterX,0)</c> is equivalent to this reflection
    /// formula). Top-side X is never affected. Deliberately separate from
    /// <see cref="MirrorXIfBottom"/> (which unconditionally mirrors an imported fiducial's raw
    /// PnP X, regardless of this checkbox) and not used for the "stack" section (feeder positions
    /// are fixed machine hardware, not board-relative) - only "comp" row X (and, transitively,
    /// mirror_create/mirror's firstX, which is a placed component's own position) respects it.</summary>
    private double MirrorXIfBottomLayersChecked(double x, BoardSide side) =>
        side == BoardSide.Bottom && MirrorBottomLayers ? 2 * _boardCenterX - x : x;

    /// <summary>Prompts for a save location and writes two NeoDen4 CSVs (base name + "_Top"/
    /// "_Bottom"), one per board side - see <see cref="WriteNeoDenStackSection"/> for what's
    /// actually written this round (just the "stack"/feeder-table section; the "comp"/placement
    /// section is deferred to a later round).</summary>
    private void GenerateNeoDenCsv()
    {
        var basePath = PromptForNeoDenSaveFile?.Invoke();
        if (string.IsNullOrEmpty(basePath)) return;

        var directory = System.IO.Path.GetDirectoryName(basePath);
        var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(basePath);
        var topPath = System.IO.Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, $"{nameNoExt}_Top.csv");
        var bottomPath = System.IO.Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, $"{nameNoExt}_Bottom.csv");

        var topSkipped = WriteNeoDenStackSection(topPath, BoardSide.Top);
        var bottomSkipped = WriteNeoDenStackSection(bottomPath, BoardSide.Bottom);

        var skippedNote = topSkipped + bottomSkipped > 0
            ? $" ({topSkipped + bottomSkipped} feeder(s) skipped - no saved position on the Settings tab)"
            : "";
        StatusMessage = $"Wrote {System.IO.Path.GetFileName(topPath)} and {System.IO.Path.GetFileName(bottomPath)}.{skippedNote}";
    }

    /// <summary>Writes the "stack" section for one board side: one row per distinct feeder number
    /// assigned to a component on that side - low-bank (&lt;50, tape feeders, position from
    /// <see cref="TapeFeederSettings"/>) and high-bank (&gt;=50, tray feeders, geometry from
    /// <see cref="TrayFeederSettings"/>) use different row layouts, matching the real NeoDen4
    /// template's own "stack,54,1,1,..." vs "stack,1,0,1,..." rows. Returns the number of
    /// assigned feeders that had to be skipped because no matching physical position exists on
    /// the Settings tab.</summary>
    private int WriteNeoDenStackSection(string path, BoardSide side)
    {
        var lines = new List<string> { NeoDenStackHeader };
        var skipped = 0;

        var lowBankFeederIds = Components
            .Where(c => c.Side == side && !c.UseHighFeederBank && c.FeederNumber is not null)
            .Select(c => c.FeederNumber!.Value)
            .Distinct()
            .OrderBy(id => id);

        foreach (var feederId in lowBankFeederIds)
        {
            // X/Y come from the physical feeder slot position (Settings tab), not from any
            // component - the feeder itself has one fixed location regardless of which part
            // currently loads it.
            var feeder = TapeFeederSettings.FirstOrDefault(f => f.Number == feederId.ToString());
            if (feeder is null) { skipped++; continue; }

            var representative = Components.First(c => c.FeederNumber == feederId);
            lines.Add(BuildLowBankStackRow(feederId, feeder, representative));
        }

        var highBankFeederIds = Components
            .Where(c => c.Side == side && c.UseHighFeederBank && c.FeederNumber is not null)
            .Select(c => c.FeederNumber!.Value)
            .Distinct()
            .OrderBy(id => id);

        foreach (var feederId in highBankFeederIds)
        {
            var tray = TrayFeederSettings.FirstOrDefault(t => t.FeederId == feederId);
            if (tray is null) { skipped++; continue; }

            var representative = Components.First(c => c.FeederNumber == feederId);
            lines.Add(BuildHighBankStackRow(feederId, tray, representative));
        }

        var fiducials = side == BoardSide.Top ? TopFiducials : BottomFiducials;
        lines.Add(BuildMarkLine(fiducials));
        lines.AddRange(NeoDenAfterMarkBoilerplateLines);

        // Only placed components have real coordinates to export - one "comp" row per component
        // (not grouped by feeder, unlike "stack"), in BOM/import order. mirror_create/mirror
        // reference the first one's own position, per explicit instruction; with none placed on
        // this side, they fall back to 0,0.
        var placedComponents = Components.Where(c => c.Side == side && c.HasPlacement).ToList();
        var (firstX, firstY) = placedComponents.Count > 0
            ? (MirrorXIfBottomLayersChecked(placedComponents[0].XMm, side), placedComponents[0].YMm)
            : (0, 0);

        lines.Add(BuildMirrorCreateLine(firstX, firstY));
        lines.Add(BuildMirrorLine(firstX, firstY));
        lines.Add(NeoDenSmdHeader);
        foreach (var component in placedComponents)
            lines.Add(BuildCompRow(component));

        File.WriteAllLines(path, lines);
        return skipped;
    }

    private static string BuildLowBankStackRow(int feederId, TapeFeederSettingViewModel feeder, ComponentViewModel representative)
    {
        var type = feederId < 50 ? 0 : 1;
        var angle = feederId < 20 ? 90 : -90;
        const double pickHeightMm = 0.5;
        const int pickDelayMs = 200;
        var placeHeightMm = 3.2 + representative.SelectedFootprint.HeightMm;
        const int placeDelayMs = 100;

        var fields = new[]
        {
            "stack",
            feederId.ToString(),
            type.ToString(),
            "1", // Nozzle - always 1 for now
            feeder.X.ToString("0.####"),
            feeder.Y.ToString("0.####"),
            angle.ToString(),
            representative.SelectedFootprint.Name,
            representative.Value ?? "",
            pickHeightMm.ToString("0.#"),
            pickDelayMs.ToString(),
            placeHeightMm.ToString("0.####"),
            placeDelayMs.ToString(),
            // Everything below is not yet computed from anything - copied as literal
            // defaults from the user's real example row (feeder 1, a low-bank row) rather
            // than guessed at, per their explicit "all other values should be as they are
            // in the example CSV" instruction.
            "No", "-40", "1", "40", "4", "50", "50", "No", "No",
            "-40", "-40", "-40", "-40", "-1", "-1", "0", "0", "", "", "",
        };
        return string.Join(",", fields.Select(EscapeNeoDenCsvField));
    }

    /// <summary>High-bank (tray) feeder row - built from the exact pattern the user supplied,
    /// with the tray's geometry (<see cref="TrayFeederSettingViewModel"/>) filling the columns
    /// that turned out (once the pattern was given) to encode Columns/Rows/EndX/EndY, not the
    /// mystery values they first looked like from a real example row alone. Footprint/Value are
    /// populated from the assigned component the same way <see cref="BuildLowBankStackRow"/>
    /// does - the pattern's own example happened to show these blank, but the user asked
    /// afterward for them to be filled in here too, matching the low-bank behavior.</summary>
    private static string BuildHighBankStackRow(int feederId, TrayFeederSettingViewModel tray, ComponentViewModel representative)
    {
        var placeHeightMm = representative.SelectedFootprint.HeightMm + 1.7;

        var fields = new[]
        {
            "stack",
            feederId.ToString(),
            "1", // Type
            "1", // Nozzle
            tray.BeginX.ToString("0.####"),
            tray.BeginY.ToString("0.####"),
            "0", // Angle
            representative.SelectedFootprint.Name,
            representative.Value ?? "",
            "1.7", // Pick height
            "200", // Pick delay
            placeHeightMm.ToString("0.####"), // Place Height
            "100", // Place Delay
            "Yes", "-40", "1", "40",
            tray.Columns.ToString(),
            tray.Rows.ToString(),
            tray.EndX.ToString("0.####"),
            tray.EndY.ToString("0.####"),
            "1", "1", "No", "No",
            "-40", "-40", "-40", "-40", "-1", "-1", "0", "0",
        };
        return string.Join(",", fields.Select(EscapeNeoDenCsvField));
    }

    private static string EscapeNeoDenCsvField(string field) =>
        field.Contains(',') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;

    /// <summary>Assigns a NeoDen4 feeder slot number to every BOM component, grouping by
    /// (Value, Footprint) so parts sharing one physical reel share one feeder number - matching
    /// how the NeoDen4 template's own "stack" (feeder) table works, where many placements
    /// reference the same Feeder ID. Unchecked groups pull from the low bank (1-40); groups
    /// where any member has <see cref="ComponentViewModel.UseHighFeederBank"/> checked pull from
    /// the high bank (54-99) instead, and every member of the group is synced to that same
    /// checkbox state and feeder number.</summary>
    private void AutoAssignFeeders()
    {
        const int lowStart = 1, lowEnd = 40;
        const int highStart = 54, highEnd = 99;

        if (Components.Count == 0)
        {
            StatusMessage = "No BOM components loaded to assign feeders to.";
            return;
        }

        var groups = Components.GroupBy(c => (Value: c.Value ?? "", Footprint: c.FootprintText ?? "")).ToList();

        var nextLow = lowStart;
        var nextHigh = highStart;
        var skippedGroups = 0;

        // Clear stale feeder labels/images before reassigning, so a feeder that no longer gets a
        // part on this run doesn't keep showing what was assigned last time.
        foreach (var feeder in TapeFeederSettings)
        {
            if (int.TryParse(feeder.Number, out var slot) && slot >= lowStart && slot <= lowEnd)
                feeder.ClearAssignedComponent();
        }
        foreach (var tray in TrayFeederSettings)
            tray.ClearAssignedComponent();

        foreach (var group in groups)
        {
            var useHighBank = group.Any(c => c.UseHighFeederBank);
            int assigned;

            if (useHighBank)
            {
                if (nextHigh > highEnd) { skippedGroups++; continue; }
                assigned = nextHigh++;

                // Draw the assigned part's to-scale footprint at the tray's start (BeginX,
                // BeginY) and its Value on the center label - only for feeder numbers that
                // correspond to an actual tray slot in TrayFeederSettings, mirroring the
                // low-bank image below.
                var tray = TrayFeederSettings.FirstOrDefault(f => f.FeederId == assigned);
                tray?.SetAssignedComponent(group.Key.Value, group.First().SelectedFootprint);
            }
            else
            {
                if (nextLow > lowEnd) { skippedGroups++; continue; }
                assigned = nextLow++;

                // Show the assigned part's name/footprint next to the feeder's number and draw
                // its to-scale footprint at the feeder's X-mark spot - only for feeder numbers
                // that correspond to an actual physical feeder slot in TapeFeederSettings.
                var feeder = TapeFeederSettings.FirstOrDefault(f => f.Number == assigned.ToString());
                feeder?.SetAssignedComponent($"{group.Key.Value}\n{group.Key.Footprint}", group.First().SelectedFootprint);
            }

            foreach (var component in group)
            {
                component.UseHighFeederBank = useHighBank;
                component.FeederNumber = assigned;
            }
        }

        StatusMessage = skippedGroups > 0
            ? $"Assigned feeders to {groups.Count - skippedGroups} of {groups.Count} unique part(s) - ran out of feeder slots for the rest."
            : $"Assigned feeders to {groups.Count} unique part(s) across {Components.Count} component(s).";

        // GenerateNeoDenCsvCommand's CanExecute depends on whether any FeederNumber is set -
        // CommandManager's automatic requery isn't guaranteed to fire promptly for a purely
        // programmatic change like this (it's driven by common UI input events), so ask
        // explicitly rather than leaving the button's enabled state to chance.
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyBottomLayerMirroring()
    {
        foreach (var layer in Layers)
        {
            if (!IsBottomRole(layer.Role)) continue;
            layer.Visual.RenderTransform = MirrorBottomLayers
                ? new ScaleTransform(-1, 1, _boardCenterX, 0)
                : Transform.Identity;
        }
    }

    private static ParsedLayer Translate(ParsedLayer layer, double dx, double dy)
    {
        if (dx == 0 && dy == 0) return layer;

        var offset = new PointMm(dx, dy);
        var primitives = layer.Primitives.Select(p => Translate(p, offset)).ToList();
        var bounds = new BoundingBox(
            layer.Bounds.MinX + dx, layer.Bounds.MinY + dy,
            layer.Bounds.MaxX + dx, layer.Bounds.MaxY + dy);

        return new ParsedLayer { Role = layer.Role, Primitives = primitives, Bounds = bounds };
    }

    private static GerberPrimitive Translate(GerberPrimitive primitive, PointMm offset) => primitive switch
    {
        SegmentPrimitive s => s with { Start = Translate(s.Start, offset), End = Translate(s.End, offset) },
        ArcPrimitive a => a with { Start = Translate(a.Start, offset), End = Translate(a.End, offset), Center = Translate(a.Center, offset) },
        FlashPrimitive f => f with { Position = Translate(f.Position, offset) },
        RegionPrimitive r => r with { Contour = r.Contour.Select(p => Translate(p, offset)).ToList() },
        _ => primitive,
    };

    private static PointMm Translate(PointMm p, PointMm offset) => new(p.X + offset.X, p.Y + offset.Y);

    private static bool IsFiducial(string designator, string? value) =>
        designator.Contains("FID", StringComparison.OrdinalIgnoreCase) ||
        (value?.Contains("Fiducial", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Reflects an untranslated X about the board's own untranslated horizontal center
    /// when <paramref name="side"/> is Bottom, leaving it untouched otherwise - the "invert X on
    /// bottom" convention fiducials imported from a PnP file use (see FiducialViewModel's docs).
    /// Only applies to that import path - a manually clicked fiducial (see
    /// <see cref="PendingFiducialPick"/>) stores the raw world/screen coordinate directly instead,
    /// with no offset or mirror applied, per explicit user correction: there's no PnP-file value
    /// to reconcile with for a point the user just clicked on screen.</summary>
    private double MirrorXIfBottom(double untranslatedX, BoardSide side) =>
        side == BoardSide.Bottom ? 2 * (_boardCenterX - _boardOffsetX) - untranslatedX : untranslatedX;

    private static string DefaultNameFor(GerberLayerRole role) => role switch
    {
        GerberLayerRole.TopPaste => "Top Paste",
        GerberLayerRole.BottomPaste => "Bottom Paste",
        GerberLayerRole.TopSoldermask => "Top Soldermask",
        GerberLayerRole.BottomSoldermask => "Bottom Soldermask",
        GerberLayerRole.Outline => "Outline",
        _ => role.ToString(),
    };

    private static Brush DefaultColorFor(GerberLayerRole role) => role switch
    {
        GerberLayerRole.TopPaste => BuildShinyGoldBrush(),
        GerberLayerRole.BottomPaste => BuildShinyGoldBrush(),
        GerberLayerRole.TopSoldermask => Brushes.White,
        GerberLayerRole.BottomSoldermask => Brushes.White,
        GerberLayerRole.Outline => GerberRenderer.PcbGreen,
        _ => Brushes.Gray,
    };

    private static bool IsBottomRole(GerberLayerRole role) =>
        role is GerberLayerRole.BottomPaste or GerberLayerRole.BottomSoldermask or GerberLayerRole.BottomComponents;
}
