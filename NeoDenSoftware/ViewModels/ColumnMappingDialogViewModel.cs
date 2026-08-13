using System.Collections.ObjectModel;

namespace NeoDenSoftware.ViewModels;

public sealed class ColumnMappingDialogViewModel : ViewModelBase
{
    public const string NoneOption = "(None)";

    public required string Title { get; init; }
    public required string Description { get; init; }
    public ObservableCollection<ColumnMappingFieldViewModel> Fields { get; } = [];

    public void AddRequired(string fieldName, IReadOnlyList<string> columns, string? guess) =>
        Fields.Add(new ColumnMappingFieldViewModel
        {
            FieldName = fieldName,
            IsRequired = true,
            AvailableColumns = columns,
            SelectedColumn = guess,
        });

    public void AddOptional(string fieldName, IReadOnlyList<string> columns, string? guess) =>
        Fields.Add(new ColumnMappingFieldViewModel
        {
            FieldName = fieldName,
            IsRequired = false,
            AvailableColumns = [NoneOption, .. columns],
            SelectedColumn = guess ?? NoneOption,
        });

    public string? Get(string fieldName)
    {
        var value = Fields.First(f => f.FieldName == fieldName).SelectedColumn;
        return string.IsNullOrEmpty(value) || value == NoneOption ? null : value;
    }
}
