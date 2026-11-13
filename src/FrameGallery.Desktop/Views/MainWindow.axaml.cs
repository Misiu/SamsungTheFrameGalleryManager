using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Desktop.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;

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
            Title += " (demo)";
        }
        else store = new TvStore(Path.Combine(AppPaths.Dir, "tvs.json"), SecretProtectors.Create());
        DataContext = _vm = new MainViewModel(store, new WindowInteraction(this), demo);

        // A click on a TV opens its gallery.
        TvList.Tapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is TvItemViewModel tv) _vm.OpenCommand.Execute(tv);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && TvList.SelectedItem is TvItemViewModel tv) _vm.RemoveCommand.Execute(tv);
        };
    }

    void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (TvList.SelectedItem is TvItemViewModel tv) _vm.RemoveCommand.Execute(tv);
    }
}
