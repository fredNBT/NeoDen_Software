using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;
using NeoDenSoftware.ViewModels;
using NeoDenSoftware.Views;

namespace NeoDenSoftware;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;
    private Point? _lastPanPosition;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.PromptForZipFile = PromptForZipFile;
        _viewModel.PromptForBomFile = () => PromptForCsvFile("Import BOM", "BOM CSV file");
        _viewModel.PromptForPnpFile = () => PromptForCsvFile("Import Pick-and-Place File", "PnP/centroid CSV file");
        _viewModel.ConfirmLayerAssignments = ConfirmLayerAssignments;
        _viewModel.ConfirmBomColumnMapping = ConfirmBomColumnMapping;
        _viewModel.ConfirmPnpColumnMapping = ConfirmPnpColumnMapping;
        _viewModel.PromptForNeoDenSaveFile = PromptForNeoDenSaveFile;
        _viewModel.PromptForSaveProjectFile = PromptForSaveProjectFile;
        _viewModel.PromptForLoadProjectFile = PromptForLoadProjectFile;
        _viewModel.LayersImported += OnLayersImported;
        _viewModel.Layers.CollectionChanged += Layers_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Permanent scene furniture - not tied to Layers at all, so no visibility toggle and no
        // ordering dependency on the CollectionChanged subscription above (that footgun only
        // applied when fixtures went through the Layers collection).
        foreach (var fixture in MainViewModel.BuildFixtureVisuals())
            FixturesHost.Children.Add(fixture);
        foreach (var feeder in _viewModel.TapeFeederSettings)
            FixturesHost.Children.Add(feeder.Visual);
        // Subscribed *after* the initial population above (mirrors Layers_CollectionChanged's own
        // ordering) - TapeFeederSettings can grow/shrink later via "+ Add"/"Remove" on the
        // Settings tab, so newly added/removed feeders need their Visual kept in sync with
        // FixturesHost for the rest of the session, not just at startup.
        _viewModel.TapeFeederSettings.CollectionChanged += TapeFeederSettings_CollectionChanged;
        foreach (var tray in _viewModel.TrayFeederSettings)
            FixturesHost.Children.Add(tray.Visual);
        _viewModel.TrayFeederSettings.CollectionChanged += TrayFeederSettings_CollectionChanged;

        ViewportBorder.Background = GerberRenderer.CanvasBackground;
        UpdateTransform();

        // Nothing has been imported yet at construction time, so there's no LayersImported event
        // to center the camera - without this, the fixtures render far outside the default view
        // and the canvas looks empty. Wait for Loaded so ViewportBorder has its real size instead
        // of a guessed one.
        Loaded += (_, _) => OnLayersImported(this, _viewModel.AllFixtureBounds);
    }

    private void ManageFootprintLibrary_Click(object sender, RoutedEventArgs e)
    {
        var window = new FootprintLibraryWindow(new FootprintLibraryViewModel()) { Owner = this };
        window.ShowDialog();
    }

    private void TapeFeederPositions_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = SettingsTabItem;
    }

    private void TrayStlGeneratorSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new TrayStlSettingsWindow(new TrayStlSettingsViewModel()) { Owner = this };
        window.ShowDialog();
    }

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        var window = new AppearanceSettingsWindow(new AppearanceSettingsViewModel()) { Owner = this };
        window.ShowDialog();
    }

    private void BoardOrigin_Click(object sender, RoutedEventArgs e)
    {
        var window = new BoardOriginWindow { Owner = this, DataContext = _viewModel };
        window.ShowDialog();
    }

    // Runs before AutoAssignFeedersCommand/GenerateNeoDenCsvCommand (Button.OnClick raises the
    // Click event before executing the bound Command) - forces any pending DataGrid edit (a
    // Feeder number just typed, a Tray Feeder box just checked) to commit to the underlying
    // ComponentViewModel first. Without this, clicking straight from an edit-in-progress cell
    // into one of these buttons - without Tab/Enter/clicking elsewhere first - can leave the
    // typed value sitting in the edit control, unread by the command that fires right after.
    private void CommitFeederSetupEdits_Click(object sender, RoutedEventArgs e)
    {
        FeederSetupDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FeederSetupDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void CombineSelectedGroups_Click(object sender, RoutedEventArgs e)
    {
        CommitFeederSetupEdits_Click(sender, e);
        var selected = FeederSetupDataGrid.SelectedItems.Cast<BomGroupViewModel>().ToList();
        _viewModel.CombineGroups(selected);
    }

    private void UndoCombine_Click(object sender, RoutedEventArgs e) => _viewModel.UndoCombine();

    /// <summary>WPF's default DataGridRow behavior treats a right-click like a left-click for
    /// selection purposes - right-clicking any one row of an existing Ctrl/Shift multi-selection
    /// would otherwise collapse it down to just that row before the context menu even opens,
    /// silently breaking "select several rows, right-click, Combine". Right-clicking a row that's
    /// already part of the current selection now leaves the whole selection alone; right-clicking
    /// outside it collapses to just that row, matching Explorer/Excel-style context-menu behavior.</summary>
    private void FeederRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row || row.IsSelected) return;

        FeederSetupDataGrid.SelectedItems.Clear();
        row.IsSelected = true;
    }

    // Same uncommitted-edit class of bug as CommitFeederSetupEdits_Click above, but for the
    // Settings tab's two feeder-position grids: typing a value into a cell and then closing the
    // window directly (Alt+F4, the X button) - without Tab/Enter/clicking elsewhere first to
    // commit the cell - never runs the bound property's setter, so FeederPositionStore.Save()
    // never fires and the edit is silently lost, even though the store itself works correctly for
    // any edit that *did* commit. Force-commit both grids right before the window actually closes.
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        TapeFeederSettingsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TapeFeederSettingsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        TrayFeederSettingsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TrayFeederSettingsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private string? PromptForNeoDenSaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create NeoDen4 File",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "NeoDen4.csv",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PromptForSaveProjectFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Project",
            Filter = "NeoDen Project (*.pnp)|*.pnp|All files (*.*)|*.*",
            DefaultExt = ".pnp",
            FileName = "myProject.pnp",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PromptForLoadProjectFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Project",
            Filter = "NeoDen Project (*.pnp)|*.pnp|All files (*.*)|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PromptForZipFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Gerber Zip",
            Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PromptForCsvFile(string title, string fileDescription)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{fileDescription} (*.csv)|*.csv|All files (*.*)|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private IReadOnlyList<ExtractedFile>? ConfirmLayerAssignments(IReadOnlyList<ExtractedFile> files)
    {
        var vm = new LayerConfirmationViewModel(files);
        var dialog = new LayerConfirmationDialog(vm) { Owner = this };
        if (dialog.ShowDialog() != true) return null;

        var byPath = files.ToDictionary(f => f.FullPath);
        foreach (var row in vm.Files)
        {
            if (byPath.TryGetValue(row.FullPath, out var file))
                file.DetectedRole = row.Role;
        }

        return files;
    }

    private BomColumnMapping? ConfirmBomColumnMapping(IReadOnlyList<string> columns, BomColumnMapping guess)
    {
        var vm = new ColumnMappingDialogViewModel
        {
            Title = "Confirm BOM Columns",
            Description = "Confirm which column in the BOM file maps to each field.",
        };
        vm.AddRequired("Designator", columns, guess.DesignatorColumn);
        vm.AddOptional("Value", columns, guess.ValueColumn);
        vm.AddOptional("Footprint", columns, guess.FootprintColumn);

        var dialog = new ColumnMappingDialog(vm) { Owner = this };
        if (dialog.ShowDialog() != true) return null;

        return new BomColumnMapping(vm.Get("Designator"), vm.Get("Value"), vm.Get("Footprint"));
    }

    private PnpColumnMapping? ConfirmPnpColumnMapping(IReadOnlyList<string> columns, PnpColumnMapping guess)
    {
        var vm = new ColumnMappingDialogViewModel
        {
            Title = "Confirm Pick-and-Place Columns",
            Description = "Confirm which column in the pick-and-place file maps to each field.",
        };
        vm.AddRequired("Designator", columns, guess.DesignatorColumn);
        vm.AddRequired("X Position", columns, guess.XColumn);
        vm.AddRequired("Y Position", columns, guess.YColumn);
        vm.AddOptional("Rotation", columns, guess.RotationColumn);
        vm.AddOptional("Layer/Side", columns, guess.SideColumn);
        // Always unchecked by default (guess.InvertY is never auto-suggested - see
        // PnpImportService.ReadHeader's comment: whether a file's raw Y values are mostly negative
        // turned out NOT to reliably mean its Y axis needs flipping, since a board's own native
        // Gerber coordinates can themselves be negative). Shown so the user has an escape hatch if
        // they genuinely do hit a tool whose PnP export inverts Y relative to its own Gerbers.
        vm.AddToggle("Invert Y axis", guess.InvertY,
            "Some EDA tools report Y with the opposite sign from their own Gerber output. Only check this if imported components land off the board in a way that flipping Y would fix - most files, including most KiCad exports, do not need this.");

        var dialog = new ColumnMappingDialog(vm) { Owner = this };
        if (dialog.ShowDialog() != true) return null;

        return new PnpColumnMapping(vm.Get("Designator"), vm.Get("X Position"), vm.Get("Y Position"), vm.Get("Rotation"), vm.Get("Layer/Side"), vm.GetToggle("Invert Y axis"));
    }

    private void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            LayerHost.Children.Clear();
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (LayerViewModel layer in e.OldItems)
                LayerHost.Children.Remove(layer.Visual);
        }

        if (e.NewItems is not null)
        {
            foreach (LayerViewModel layer in e.NewItems)
                LayerHost.Children.Add(layer.Visual);
        }
    }

    private void TapeFeederSettings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TapeFeederSettingViewModel feeder in e.OldItems)
                FixturesHost.Children.Remove(feeder.Visual);
        }

        if (e.NewItems is not null)
        {
            foreach (TapeFeederSettingViewModel feeder in e.NewItems)
                FixturesHost.Children.Add(feeder.Visual);
        }
    }

    private void TrayFeederSettings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TrayFeederSettingViewModel tray in e.OldItems)
                FixturesHost.Children.Remove(tray.Visual);
        }

        if (e.NewItems is not null)
        {
            foreach (TrayFeederSettingViewModel tray in e.NewItems)
                FixturesHost.Children.Add(tray.Visual);
        }
    }

    private void OnLayersImported(object? sender, BoundingBox bounds)
    {
        var viewportWidth = ViewportBorder.ActualWidth > 0 ? ViewportBorder.ActualWidth : 800;
        var viewportHeight = ViewportBorder.ActualHeight > 0 ? ViewportBorder.ActualHeight : 500;

        _scale = 1.0; // 1 mm = 1 device-independent pixel at the default zoom level
        _offsetX = viewportWidth / 2 - _scale * (bounds.MinX + bounds.MaxX) / 2;
        _offsetY = viewportHeight / 2 + _scale * (bounds.MinY + bounds.MaxY) / 2;
        UpdateTransform();
    }

    private void ViewportBorder_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var position = e.GetPosition(ViewportBorder);
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;

        var worldX = (position.X - _offsetX) / _scale;
        var worldY = (_offsetY - position.Y) / _scale;

        _scale = Math.Clamp(_scale * factor, 0.02, 200);
        _offsetX = position.X - _scale * worldX;
        _offsetY = position.Y + _scale * worldY;

        UpdateTransform();
        e.Handled = true;
    }

    private void ViewportBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(ViewportBorder);

        if (_viewModel.PendingFiducialPick is { } fiducial)
        {
            // Stored as-is - the raw world/screen coordinate the user clicked, same frame the
            // mouse X/Y status bar readout uses. No board-offset or bottom-mirror correction here
            // (unlike fiducials auto-detected from a PnP file): there's no PnP-file value to
            // reconcile with for a point the user just clicked, per explicit user correction.
            fiducial.X = (position.X - _offsetX) / _scale;
            fiducial.Y = (_offsetY - position.Y) / _scale;
            _viewModel.PendingFiducialPick = null;
            e.Handled = true;
            return;
        }

        _lastPanPosition = position;
        ViewportBorder.CaptureMouse();
        ViewportBorder.Focus();

        _viewModel.SelectedComponent = HitTestComponent(e.GetPosition(LayerHost));
    }

    private ComponentViewModel? HitTestComponent(Point pointInLayerHostSpace)
    {
        var result = VisualTreeHelper.HitTest(LayerHost, pointInLayerHostSpace);

        var current = result?.VisualHit;
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: ComponentViewModel component })
                return component;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // Window-level (not ViewportBorder-level) since right after clicking the sidebar's "+ Add"
    // button, keyboard focus is on that button, not the viewport - Escape needs to cancel
    // pick-mode regardless of which control currently has focus.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.PendingFiducialPick is not null)
        {
            _viewModel.PendingFiducialPick = null;
            e.Handled = true;
            return;
        }

        // Only intercepts Ctrl+Z when there's actually a combine to undo, so it doesn't steal a
        // TextBox's own native text-undo (e.g. mid-edit in the Feeder Setup grid's Feeder column)
        // when there's nothing of ours to restore.
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.CanUndoCombine)
        {
            FeederSetupDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            FeederSetupDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _viewModel.UndoCombine();
            e.Handled = true;
        }
    }

    private void ViewportBorder_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (_viewModel.SelectedComponent is null) return;

        _viewModel.RotateSelectedComponent(90);
        e.Handled = true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedComponent))
            UpdateComponentInfoPanel();

        if (e.PropertyName == nameof(MainViewModel.PendingFiducialPick))
        {
            ViewportBorder.Cursor = _viewModel.PendingFiducialPick is not null ? Cursors.Cross : Cursors.Arrow;
            StatusBarPickHint.Text = _viewModel.PendingFiducialPick is not null
                ? "Click the board to set the new fiducial's position, or press Esc to cancel."
                : "";
        }
    }

    private void UpdateComponentInfoPanel()
    {
        var component = _viewModel.SelectedComponent;
        if (component is null)
        {
            ComponentInfoPanel.Visibility = Visibility.Collapsed;
            ComponentInfoPreview.Content = null;
            return;
        }

        var color = component.Side == BoardSide.Top ? Brushes.DeepSkyBlue : Brushes.Orange;
        ComponentInfoPanel.Visibility = Visibility.Visible;
        ComponentInfoDesignator.Text = component.Designator;
        ComponentInfoComment.Text = component.Value ?? "(no value)";
        ComponentInfoPosition.Text = $"X: {component.XMm:F2}  Y: {component.YMm:F2}  Rot: {component.RotationDegrees:F0}°";
        ComponentInfoPreview.Content = component.BuildScaledPreview(44, color);
    }

    private void ViewportBorder_MouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(ViewportBorder);

        if (_lastPanPosition is { } lastPosition)
        {
            _offsetX += position.X - lastPosition.X;
            _offsetY += position.Y - lastPosition.Y;
            _lastPanPosition = position;
            UpdateTransform();
        }

        var worldX = (position.X - _offsetX) / _scale;
        var worldY = (_offsetY - position.Y) / _scale;
        MouseXText.Text = $"X: {worldX:F2}";
        MouseYText.Text = $"Y: {worldY:F2}";
    }

    private void ViewportBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _lastPanPosition = null;
        ViewportBorder.ReleaseMouseCapture();
    }

    private void ViewportBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        MouseXText.Text = "X: --";
        MouseYText.Text = "Y: --";
    }

    private void UpdateTransform()
    {
        var matrix = new MatrixTransform(_scale, 0, 0, -_scale, _offsetX, _offsetY);
        LayerHost.RenderTransform = matrix;
        FixturesHost.RenderTransform = matrix;
    }
}
