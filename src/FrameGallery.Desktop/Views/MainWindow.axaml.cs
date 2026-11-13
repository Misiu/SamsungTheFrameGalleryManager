using Avalonia.Controls;
using Avalonia.Input;
using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Desktop.Views;

/// <summary>The only window. It owns the shell, which switches between the screens inside it.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Demo mode (FrameGallery --demo): a throw-away list with one fake TV, no network and a fake gallery.
        var demo = Environment.GetCommandLineArgs().Contains("--demo") || Environment.GetEnvironmentVariable("FRAMEGALLERY_DEMO") == "1";
        TvStore store;
        if (demo)
        {
            var path = Path.Combine(Path.GetTempPath(), "framegallery-demo-tvs.json");
            File.Delete(path);
            store = new TvStore(path, new UnencryptedSecretProtector());
            store.SetToken(store.Upsert(new TvInfo("demo-1", "65\" The Frame (demo)", "QE65LS03F", "192.168.1.99", true, 3840, 2160), null), "demo");
        }
        else store = new TvStore(Path.Combine(AppPaths.Dir, "tvs.json"), SecretProtectors.Create());

        var shell = new ShellViewModel(demo ? "Frame Gallery (demo)" : "Frame Gallery");
        _ = shell.ShowAsync(new TvListViewModel(store, new WindowInteraction(this, shell), demo));
        DataContext = shell;

        // Back: Esc, Alt+Left, or the "back" button of a mouse.
        KeyDown += (_, e) =>
        {
            if (e.Handled || (e.Key != Key.Escape && !(e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt))) return;
            shell.GoBack();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.XButton1) return;
            shell.GoBack();
            e.Handled = true;
        };
    }
}
