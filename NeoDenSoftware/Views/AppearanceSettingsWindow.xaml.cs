using System.Windows;
using NeoDenSoftware.ViewModels;

namespace NeoDenSoftware.Views;

public partial class AppearanceSettingsWindow : Window
{
    public AppearanceSettingsWindow(AppearanceSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
