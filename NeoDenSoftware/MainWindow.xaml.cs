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
        foreach (var tray in _viewModel.TrayFeederSettings)
            FixturesHost.Children.Add(tray.Visual);

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

    private void BoardOrigin_Click(object sender, RoutedEventArgs e)
    {
        var window = new BoardOriginWindow { Owner = this, DataContext = _viewModel };
        window.ShowDialog();
    }

    // Runs before AutoAssignFeedersCommand/GenerateNeoDenCsvCommand (Button.OnClick raises the
    // Click event before executing the bound Command) - forces any pending DataGrid edit (a
    // Feeder number just typed, a High Bank box just checked) to commit to the underlying
    // ComponentViewModel first. Without this, clicking straight from an edit-in-progress cell
    // into one of these buttons - without Tab/Enter/clicking elsewhere first - can leave the
    // typed value sitting in the edit control, unread by the command that fires right after.
    private void CommitFeederSetupEdits_Click(object sender, RoutedEventArgs e)
    {
        FeederSetupDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FeederSetupDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
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

        var dialog = new ColumnMappingDialog(vm) { Owner = this };
        if (dialog.ShowDialog() != true) return null;

        return new PnpColumnMapping(vm.Get("Designator"), vm.Get("X Position"), vm.Get("Y Position"), vm.Get("Rotation"), vm.Get("Layer/Side"));
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
        if (e.Key != Key.Escape || _viewModel.PendingFiducialPick is null) return;

        _viewModel.PendingFiducialPick = null;
        e.Handled = true;
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
