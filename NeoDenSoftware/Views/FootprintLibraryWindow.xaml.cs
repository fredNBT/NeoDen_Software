using System.Windows;
using Microsoft.Win32;
using NeoDenSoftware.ViewModels;

namespace NeoDenSoftware.Views;

public partial class FootprintLibraryWindow : Window
{
    public FootprintLibraryWindow(FootprintLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Footprint Image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            ((FootprintLibraryViewModel)DataContext).NewImagePath = dialog.FileName;
    }

    private void UploadStl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Footprint STL",
            Filter = "STL files (*.stl)|*.stl|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            ((FootprintLibraryViewModel)DataContext).SetStlPath(dialog.FileName);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
