using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;
using NeoDenSoftware.Appearance;

namespace NeoDenSoftware
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Kept as a field (not a local) so the OS handle stays open, and the lock held, for the
        // whole app lifetime - a local Mutex variable could be garbage-collected (and its lock
        // silently released) while the app is still running.
        private Mutex? _singleInstanceMutex;

        // Two copies of this app running at once both load the custom footprint library into
        // memory once at their own startup, then each independently reads-modifies-writes
        // custom_footprints.json on every Add/Edit (see CustomFootprintStore's own cross-process
        // Mutex around that). That store-level lock stops a torn/interleaved write, but a second
        // running copy is still confusing and easy to end up with by accident (a stray previous
        // launch, a shortcut double-clicked twice) - refusing to start a second copy removes the
        // whole scenario instead of just making it safe.
        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: "NeoDenSoftware_SingleInstanceLock", createdNew: out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "NeoDenSoftware is already running. Close the other copy first - running two at once can cause saved data (like the footprint library) to be lost.",
                    "NeoDenSoftware", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            // Applied before base.OnStartup(e) - that's what raises the Startup event StartupUri's
            // automatic MainWindow creation listens for, so the theme dictionary must already be
            // merged into Application.Resources before MainWindow's first layout pass, or the
            // window would flash unstyled for a frame.
            ThemeManager.Apply(ThemeSettingsStore.Load());
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }

}
