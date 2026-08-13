using System.Windows;

namespace NeoDenSoftware.Views;

/// <summary>
/// Small editor for MainViewModel.BoardOriginX/Y - deliberately has no ViewModel of its own,
/// binding directly to whatever MainViewModel instance the caller assigns as DataContext, since
/// those two properties already live there and there's nothing else this window needs to show.
/// </summary>
public partial class BoardOriginWindow : Window
{
    public BoardOriginWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
