using System.Configuration;
using System.Data;
using System.Windows;
using NeoDenSoftware.Appearance;

namespace NeoDenSoftware
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Applied before base.OnStartup(e) - that's what raises the Startup event StartupUri's
        // automatic MainWindow creation listens for, so the theme dictionary must already be
        // merged into Application.Resources before MainWindow's first layout pass, or the window
        // would flash unstyled for a frame.
        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.Apply(ThemeSettingsStore.Load());
            base.OnStartup(e);
        }
    }

}
