using System.Windows;
using NeoDenSoftware.ViewModels;

namespace NeoDenSoftware.Views;

public partial class ColumnMappingDialog : Window
{
    public ColumnMappingDialog(ColumnMappingDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ColumnMappingDialogViewModel)DataContext;
        if (vm.Fields.Any(f => f.IsRequired && string.IsNullOrEmpty(f.SelectedColumn)))
        {
            MessageBox.Show(this, "Please select a column for every required (*) field.", "Missing mapping",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
