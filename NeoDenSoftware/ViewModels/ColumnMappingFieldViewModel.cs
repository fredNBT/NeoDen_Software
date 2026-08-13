namespace NeoDenSoftware.ViewModels;

/// <summary>One field to map (e.g. "Designator") to a column in the user's CSV file.</summary>
public sealed class ColumnMappingFieldViewModel : ViewModelBase
{
    private string? _selectedColumn;

    public required string FieldName { get; init; }
    public required bool IsRequired { get; init; }
    public required IReadOnlyList<string> AvailableColumns { get; init; }
    public string DisplayName => IsRequired ? $"{FieldName} *" : FieldName;

    public string? SelectedColumn
    {
        get => _selectedColumn;
        set => SetField(ref _selectedColumn, value);
    }
}
