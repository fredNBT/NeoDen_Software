using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.ViewModels;

/// <summary>
/// Backs the Footprint Library window (File menu): lists every known footprint (built-in and
/// user-added) and lets the user add a new one - one or more comma-separated names, L/W/H in mm,
/// and an uploaded image - or edit an existing custom one. Built-in footprints are hardcoded in
/// source and can't be edited; selecting one just clears back to add mode. New/edited entries are
/// persisted immediately via <see cref="FootprintLibrary"/> so they stay in the library across
/// app restarts.
/// </summary>
public sealed class FootprintLibraryViewModel : ViewModelBase
{
    private string _newNames = string.Empty;
    private double _newLengthMm = 1.0;
    private double _newWidthMm = 1.0;
    private double _newHeightMm = 1.0;
    private string? _newImagePath;
    private string? _statusMessage;
    private FootprintDefinition? _selectedFootprint;
    private FootprintDefinition? _editingFootprint;

    public ObservableCollection<FootprintDefinition> Footprints => FootprintLibrary.All;

    /// <summary>The DataGrid's selected row. Selecting a custom footprint loads it into the form
    /// below for editing; selecting a built-in (or nothing) clears back to add mode.</summary>
    public FootprintDefinition? SelectedFootprint
    {
        get => _selectedFootprint;
        set
        {
            if (!SetField(ref _selectedFootprint, value)) return;
            if (value is { IsCustom: true }) BeginEdit(value);
            else CancelEdit();
        }
    }

    public bool IsEditing => _editingFootprint is not null;
    public string FormTitle => IsEditing ? $"Edit '{_editingFootprint!.Name}'" : "Add New Footprint";
    public string SaveButtonText => IsEditing ? "Save Changes" : "Add to Library";

    /// <summary>One or more names for this footprint, comma-separated (e.g. "0805, 805") - the
    /// first is used as the primary/display name, the rest as aliases. Any of them will match
    /// this footprint during BOM import (<see cref="FootprintMatcher"/>).</summary>
    public string NewNames
    {
        get => _newNames;
        set => SetField(ref _newNames, value);
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

    /// <summary>Set by the view's "Browse Image..." button (OpenFileDialog is UI-specific, so
    /// the view sets this directly rather than the ViewModel prompting for it itself). In add
    /// mode this must be set before saving; in edit mode, leaving it null keeps the existing
    /// image - only a newly chosen file replaces it.</summary>
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
        NewImagePath ?? (IsEditing ? "(keep current image)" : "(no image chosen)");

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelEditCommand { get; }

    public FootprintLibraryViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
    }

    private void BeginEdit(FootprintDefinition footprint)
    {
        _editingFootprint = footprint;
        NewNames = footprint.DisplayNames;
        NewLengthMm = footprint.LengthMm;
        NewWidthMm = footprint.WidthMm;
        NewHeightMm = footprint.HeightMm;
        NewImagePath = null;
        StatusMessage = null;
        NotifyEditModeChanged();
    }

    public void CancelEdit()
    {
        _editingFootprint = null;
        _selectedFootprint = null;
        NewNames = string.Empty;
        NewLengthMm = 1.0;
        NewWidthMm = 1.0;
        NewHeightMm = 1.0;
        NewImagePath = null;
        StatusMessage = null;
        OnPropertyChanged(nameof(SelectedFootprint));
        NotifyEditModeChanged();
    }

    private void NotifyEditModeChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(ImagePathDisplay));
    }

    private static List<string> ParseNames(string raw) =>
        raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Save()
    {
        var names = ParseNames(NewNames);
        if (names.Count == 0)
        {
            StatusMessage = "Enter at least one name for the footprint (comma-separated for more than one).";
            return;
        }

        if (NewLengthMm <= 0 || NewWidthMm <= 0 || NewHeightMm <= 0)
        {
            StatusMessage = "Length, width, and height must all be greater than zero.";
            return;
        }

        // Every proposed name/alias must be free across the whole library (as either another
        // entry's primary name or one of its aliases), excluding the entry currently being
        // edited (identified by its original, pre-edit name) so saving without renaming doesn't
        // flag itself as a collision.
        foreach (var name in names)
        {
            var collision = Footprints.FirstOrDefault(f =>
                (_editingFootprint is null || !string.Equals(f.Name, _editingFootprint.Name, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 f.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))));
            if (collision is not null)
            {
                StatusMessage = $"'{name}' is already used by '{collision.Name}'.";
                return;
            }
        }

        var primaryName = names[0];
        var aliases = names.Skip(1).ToList();

        if (_editingFootprint is { } editing)
        {
            FootprintLibrary.UpdateCustom(editing, primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewImagePath);
            StatusMessage = $"Updated '{primaryName}'.";
        }
        else
        {
            if (string.IsNullOrEmpty(NewImagePath) || !File.Exists(NewImagePath))
            {
                StatusMessage = "Choose an image file for the new footprint.";
                return;
            }

            FootprintLibrary.AddCustom(primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewImagePath);
            StatusMessage = $"Added '{primaryName}' to the library.";
        }

        var savedMessage = StatusMessage;
        CancelEdit();
        StatusMessage = savedMessage;
    }
}
