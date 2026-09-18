namespace NeoDenSoftware.ViewModels;

/// <summary>One yes/no option shown in the column-mapping dialog alongside the field dropdowns
/// (e.g. "Invert Y axis") - separate from <see cref="ColumnMappingFieldViewModel"/> since it isn't
/// a column choice at all, just a plain checkbox.</summary>
public sealed class ColumnMappingToggleViewModel : ViewModelBase
{
    private bool _isChecked;

    public required string Label { get; init; }
    public string? Tooltip { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}
