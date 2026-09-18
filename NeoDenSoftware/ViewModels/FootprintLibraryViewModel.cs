using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using NeoDenSoftware.Footprints;
using NeoDenSoftware.Models;
using NeoDenSoftware.Stl;

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
    private string? _newStlPath;
    private FootprintShapeKind _newShapeKind = FootprintShapeKind.TwoTerminalChip;
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
            if (value is not null) _newStlPath = null;
            OnPropertyChanged(nameof(ImagePathDisplay));
            OnPropertyChanged(nameof(StlPathDisplay));
        }
    }

    public string ImagePathDisplay =>
        NewImagePath ?? (IsEditing && _editingFootprint?.StlSourcePath is null ? "(keep current image)" : "(no image chosen)");

    /// <summary>Set by the view's "Upload STL..." button. Mutually exclusive with
    /// <see cref="NewImagePath"/> - a footprint is either a flat photo or a baked STL render, never
    /// both, so setting one clears the other. In add mode this (or <see cref="NewImagePath"/>) must
    /// be set before saving; in edit mode, leaving both null keeps whichever asset the footprint
    /// already has.</summary>
    public string? NewStlPath
    {
        get => _newStlPath;
        set
        {
            if (!SetField(ref _newStlPath, value)) return;
            if (value is not null) _newImagePath = null;
            OnPropertyChanged(nameof(NewImagePath));
            OnPropertyChanged(nameof(ImagePathDisplay));
            OnPropertyChanged(nameof(StlPathDisplay));
        }
    }

    public string StlPathDisplay =>
        NewStlPath ?? (IsEditing && _editingFootprint?.StlSourcePath is not null ? "(keep current STL render)" : "(no STL chosen)");

    /// <summary>Every shape a footprint can be drawn as when it has no image/STL - same set the
    /// built-in library uses (<see cref="FootprintShapeKind.Custom"/> excluded - that value means
    /// "has an image/STL render", not a procedural shape choice).</summary>
    public static IReadOnlyList<FootprintShapeKind> ShapeKindOptions { get; } =
        Enum.GetValues<FootprintShapeKind>().Where(k => k != FootprintShapeKind.Custom).ToList();

    /// <summary>Which procedural placeholder outline to draw when the footprint being added/edited
    /// has no image and no STL (see <see cref="ShapeKindOptions"/>) - ignored otherwise.</summary>
    public FootprintShapeKind NewShapeKind
    {
        get => _newShapeKind;
        set => SetField(ref _newShapeKind, value);
    }

    /// <summary>Parses the chosen STL immediately so the user gets fast feedback on a malformed
    /// file (rather than only finding out on Save), and auto-fills L/W/H from the mesh's bounding
    /// box - still user-editable afterward, same as a manually typed value.</summary>
    public void SetStlPath(string path)
    {
        StlMesh mesh;
        try
        {
            mesh = StlParser.Parse(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read that STL file: {ex.Message}";
            return;
        }

        NewLengthMm = Math.Round(mesh.SizeX, 2);
        NewWidthMm = Math.Round(mesh.SizeY, 2);
        NewHeightMm = Math.Round(mesh.SizeZ, 2);
        StatusMessage = null;
        NewStlPath = path;
    }

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
        _newImagePath = null;
        _newStlPath = null;
        // A procedural (no image/STL) footprint's own ShapeKind drives its placeholder outline -
        // prefill the picker with it so re-saving without touching the shape keeps it unchanged.
        // An image/STL-based footprint's ShapeKind is always the Custom sentinel (not a real
        // choice), so fall back to the default rather than showing that non-option.
        _newShapeKind = footprint.ImagePath is null ? footprint.ShapeKind : FootprintShapeKind.TwoTerminalChip;
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
        _newImagePath = null;
        _newStlPath = null;
        _newShapeKind = FootprintShapeKind.TwoTerminalChip;
        StatusMessage = null;
        OnPropertyChanged(nameof(SelectedFootprint));
        NotifyEditModeChanged();
    }

    private void NotifyEditModeChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(NewImagePath));
        OnPropertyChanged(nameof(ImagePathDisplay));
        OnPropertyChanged(nameof(NewStlPath));
        OnPropertyChanged(nameof(StlPathDisplay));
        OnPropertyChanged(nameof(NewShapeKind));
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
            if (NewStlPath is not null)
                FootprintLibrary.UpdateCustomFromStl(editing, primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewStlPath);
            else if (NewImagePath is not null)
                FootprintLibrary.UpdateCustom(editing, primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewImagePath);
            else if (editing.ImagePath is null)
                // Already a procedural (no image/STL) footprint and no new asset was chosen -
                // re-save with the (possibly changed) shape picker rather than falling into
                // UpdateCustom's "keep current image" path, which has no image to keep here.
                FootprintLibrary.UpdateCustomProcedural(editing, primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewShapeKind);
            else
                // Has an existing image/STL and neither was replaced - keep it as-is (existing
                // "no new file chosen" behavior).
                FootprintLibrary.UpdateCustom(editing, primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, null);
            StatusMessage = $"Updated '{primaryName}'.";
        }
        else
        {
            if (NewStlPath is not null)
            {
                if (!File.Exists(NewStlPath))
                {
                    StatusMessage = "Choose an image or STL file for the new footprint.";
                    return;
                }

                FootprintLibrary.AddCustomFromStl(primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewStlPath);
            }
            else if (NewImagePath is not null)
            {
                if (!File.Exists(NewImagePath))
                {
                    StatusMessage = "Choose an image or STL file for the new footprint.";
                    return;
                }

                FootprintLibrary.AddCustom(primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewImagePath);
            }
            else
            {
                // No image, no STL - render the same procedural placeholder outline a built-in
                // footprint gets, from the shape picker and L/W/H.
                FootprintLibrary.AddCustomProcedural(primaryName, aliases, NewLengthMm, NewWidthMm, NewHeightMm, NewShapeKind);
            }

            StatusMessage = $"Added '{primaryName}' to the library.";
        }

        var savedMessage = StatusMessage;
        CancelEdit();
        StatusMessage = savedMessage;
    }
}
