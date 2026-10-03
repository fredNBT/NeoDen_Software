using System.Windows;
using Microsoft.Win32;
using NeoDenSoftware.ViewModels;

namespace NeoDenSoftware.Views;

public partial class ComponentLibraryWindow : Window
{
    public ComponentLibraryWindow(ComponentLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PromptForTrayStlFile = suggestedName =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Tray STL",
                Filter = "STL files (*.stl)|*.stl",
                FileName = suggestedName,
                DefaultExt = ".stl",
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Component Image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            ((ComponentLibraryViewModel)DataContext).NewImagePath = dialog.FileName;
    }

    private void BrowseDatasheet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Datasheet PDF",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            ((ComponentLibraryViewModel)DataContext).NewDatasheetPath = dialog.FileName;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
