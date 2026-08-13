using System.Windows;
using NeoDenSoftware.ViewModels;

namespace NeoDenSoftware.Views;

public partial class LayerConfirmationDialog : Window
{
    public LayerConfirmationDialog(LayerConfirmationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
