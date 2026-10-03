using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using NeoDenSoftware.ComponentLibrary;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// Backs the Component Library window (File menu): a general parts-reference list - name,
/// description, size, an optional photo, an optional datasheet PDF - independent of
/// <see cref="FootprintLibraryViewModel"/>. Same add/edit-in-place shape as that window: selecting
/// a row loads it into the form below for editing; Save either adds a new entry or updates the
/// selected one, persisted immediately via <see cref="ComponentLibraryStore"/>.
/// </summary>
public sealed class ComponentLibraryViewModel : ViewModelBase
{
    private string _newName = string.Empty;
    private string _newDescription = string.Empty;
    private string _newDraw = string.Empty;
    private double _newLengthMm = 1.0;
    private double _newWidthMm = 1.0;
    private double _newHeightMm = 1.0;
    private string? _newImagePath;
    private string? _newDatasheetPath;
    private string? _statusMessage;
    private ComponentLibraryEntry? _selectedEntry;
    private ComponentLibraryEntry? _editingEntry;

    /// <summary>The live, process-wide list (see <see cref="ComponentLibrary.ComponentLibraryLookup"/>)
    /// rather than a private copy - an edit made here is then immediately visible to board/BOM
    /// rendering and tray STL export, the same way <see cref="FootprintLibraryViewModel.Footprints"/>
    /// exposes <see cref="Footprints.FootprintLibrary.All"/> directly.</summary>
    public ObservableCollection<ComponentLibraryEntry> Entries => ComponentLibraryLookup.All;

    /// <summary>Shown as a plain string at the bottom of the window - see
    /// <see cref="ComponentLibraryStore.ManifestFilePath"/> for why this is surfaced directly
    /// rather than left to a code comment.</summary>
    public string ManifestFilePath => ComponentLibraryStore.ManifestFilePath;

    public ComponentLibraryEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetField(ref _selectedEntry, value)) return;
            if (value is not null) BeginEdit(value);
            else CancelEdit();
        }
    }

    public bool IsEditing => _editingEntry is not null;
    public string FormTitle => IsEditing ? $"Edit '{_editingEntry!.Name}'" : "Add New Component";
    public string SaveButtonText => IsEditing ? "Save Changes" : "Add to Library";

    public string NewName
    {
        get => _newName;
        set => SetField(ref _newName, value);
    }

    public string NewDescription
    {
        get => _newDescription;
        set => SetField(ref _newDescription, value);
    }

    /// <summary>Free text: where this part is physically stored (e.g. "Drawer 3", "Bin A1").</summary>
    public string NewDraw
    {
        get => _newDraw;
        set => SetField(ref _newDraw, value);
    }

    public double NewLengthMm
    {
        get => _newLengthMm;
        set => SetField(ref _newLengthMm, value);
    }

    public double NewWidthMm
    {
        get => _newWidthMm;
        set => SetField(ref _newWidthMm, value);
    }

    public double NewHeightMm
    {
        get => _newHeightMm;
        set => SetField(ref _newHeightMm, value);
    }

    /// <summary>Set by the view's "Browse Image..." button. In edit mode, leaving this null keeps
    /// the existing image; only a newly chosen file replaces it.</summary>
    public string? NewImagePath
    {
        get => _newImagePath;
        set
        {
            if (!SetField(ref _newImagePath, value)) return;
            OnPropertyChanged(nameof(ImagePathDisplay));
        }
    }

    public string ImagePathDisplay =>
        NewImagePath ?? (IsEditing && _editingEntry?.ImagePath is not null ? "(keep current image)" : "(no image chosen)");

    /// <summary>Set by the view's "Browse Datasheet (PDF)..." button. Same "null keeps the
    /// existing file" rule as <see cref="NewImagePath"/>.</summary>
    public string? NewDatasheetPath
    {
        get => _newDatasheetPath;
        set
        {
            if (!SetField(ref _newDatasheetPath, value)) return;
            OnPropertyChanged(nameof(DatasheetPathDisplay));
        }
    }

    public string DatasheetPathDisplay =>
        NewDatasheetPath ?? (IsEditing && _editingEntry?.DatasheetPath is not null ? "(keep current datasheet)" : "(no datasheet chosen)");

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelEditCommand { get; }

    /// <summary>Opens a row's datasheet PDF in whatever the OS has registered for PDFs - this app
    /// doesn't render PDFs itself, that would be a much bigger dependency for very little benefit
    /// over just handing it to the viewer the user already has.</summary>
    public ICommand OpenDatasheetCommand { get; }

    /// <summary>Builds the same parts-tray STL "Create NeoDen4 File..." writes for a tray-feeder
    /// part, for one library row - sized from that entry's own L/W/H (exactly what the export
    /// uses when a Component Library entry exists for a part), labeled with its name, using the
    /// current Tray STL Generator Settings.</summary>
    public ICommand CreateTrayStlCommand { get; }

    /// <summary>Set by the view: asks the user where to save, given a suggested file name;
    /// null/empty means cancelled.</summary>
    public Func<string, string?>? PromptForTrayStlFile { get; set; }

    public ComponentLibraryViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        CreateTrayStlCommand = new RelayCommand(param =>
        {
            if (param is ComponentLibraryEntry entry) CreateTrayStl(entry);
        });
        OpenDatasheetCommand = new RelayCommand(param =>
        {
            if (param is ComponentLibraryEntry { DatasheetPath: { } path } && File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        });
    }

    private void CreateTrayStl(ComponentLibraryEntry entry)
    {
        var suggested = string.Join("_", $"Tray_{entry.Name}.stl".Split(Path.GetInvalidFileNameChars()));
        var path = PromptForTrayStlFile?.Invoke(suggested);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var footprint = new FootprintDefinition(entry.Name, entry.LengthMm, entry.WidthMm, entry.HeightMm, FootprintShapeKind.Sot, 0);
            var mesh = Stl.TrayStlGenerator.BuildTray(footprint, Stl.TrayStlSettingsStore.Load(), entry.Name);
            Stl.StlWriter.WriteAscii(path, mesh);
            StatusMessage = $"Wrote tray STL for '{entry.Name}' to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create tray STL: {ex.Message}";
        }
    }

    private void BeginEdit(ComponentLibraryEntry entry)
    {
        _editingEntry = entry;
        NewName = entry.Name;
        NewDescription = entry.Description;
        NewDraw = entry.Draw;
        NewLengthMm = entry.LengthMm;
        NewWidthMm = entry.WidthMm;
        NewHeightMm = entry.HeightMm;
        _newImagePath = null;
        _newDatasheetPath = null;
        StatusMessage = null;
        NotifyEditModeChanged();
    }

    public void CancelEdit()
    {
        _editingEntry = null;
        _selectedEntry = null;
        NewName = string.Empty;
        NewDescription = string.Empty;
        NewDraw = string.Empty;
        NewLengthMm = 1.0;
        NewWidthMm = 1.0;
        NewHeightMm = 1.0;
        _newImagePath = null;
        _newDatasheetPath = null;
        StatusMessage = null;
        OnPropertyChanged(nameof(SelectedEntry));
        NotifyEditModeChanged();
    }

    private void NotifyEditModeChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(NewImagePath));
        OnPropertyChanged(nameof(ImagePathDisplay));
        OnPropertyChanged(nameof(NewDatasheetPath));
        OnPropertyChanged(nameof(DatasheetPathDisplay));
    }

    private void Save()
    {
        var name = NewName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Enter a name for the component.";
            return;
        }

        if (NewLengthMm <= 0 || NewWidthMm <= 0 || NewHeightMm <= 0)
        {
            StatusMessage = "Length, width, and height must all be greater than zero.";
            return;
        }

        var collision = Entries.FirstOrDefault(c =>
            (_editingEntry is null || !string.Equals(c.Name, _editingEntry.Name, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (collision is not null)
        {
            StatusMessage = $"'{name}' already exists in the library.";
            return;
        }

        ComponentLibraryEntry saved;
        if (_editingEntry is { } editing)
        {
            saved = ComponentLibraryStore.UpdateEntry(editing.Name, name, NewDescription, NewDraw, NewLengthMm, NewWidthMm, NewHeightMm,
                NewImagePath, NewDatasheetPath);
            var index = Entries.IndexOf(editing);
            if (index >= 0) Entries[index] = saved;
            StatusMessage = $"Updated '{name}'.";
        }
        else
        {
            saved = ComponentLibraryStore.AddEntry(name, NewDescription, NewDraw, NewLengthMm, NewWidthMm, NewHeightMm, NewImagePath, NewDatasheetPath);
            Entries.Add(saved);
            StatusMessage = $"Added '{name}' to the library.";
        }

        var savedMessage = StatusMessage;
        CancelEdit();
        StatusMessage = savedMessage;
    }
}
