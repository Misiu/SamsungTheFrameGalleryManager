using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.Desktop;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Log to a file (raw protocol traffic, secrets masked). The header records the UI language to help with bug reports.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var language = (Loc.Culture ?? CultureInfo.CurrentUICulture).Name;
        TvLog.AttachFile(AppPaths.Log, $"FrameGallery {version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}, UI language {language}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) => TvLog.Write($"[CRASH] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { TvLog.Write($"[CRASH] unobserved task exception: {e.Exception}"); e.SetObserved(); };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
