using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FrameGallery.Core;

namespace FrameGallery.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // An exception in an event handler is logged instead of ending the application.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            TvLog.Write($"[CRASH] exception on the UI thread: {e.Exception}");
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Views.MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
