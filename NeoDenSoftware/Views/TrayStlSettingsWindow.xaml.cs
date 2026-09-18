using System.Windows;
using NeoDenSoftware.ViewModels;

namespace NeoDenSoftware.Views;

public partial class TrayStlSettingsWindow : Window
{
    public TrayStlSettingsWindow(TrayStlSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
