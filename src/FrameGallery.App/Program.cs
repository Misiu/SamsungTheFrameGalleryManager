using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.App;

static class AppPaths
{
    /// <summary>User data (the TV list, encrypted tokens): %APPDATA%\FrameGallery.</summary>
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameGallery");

    /// <summary>The log folder: a "logs" subfolder of the application folder (easy to find and to attach to a bug report).</summary>
    public static string LogDir { get; } = ResolveLogDir();
    public static string Log => Path.Combine(LogDir, "log.txt");

    /// <summary>If the application folder is read-only (e.g. Program Files), logs go to %LOCALAPPDATA%\FrameGallery\logs.</summary>
    static string ResolveLogDir()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameGallery", "logs"),
        ];
        foreach (var dir in candidates)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return Path.GetTempPath();
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Log to a file (raw protocol traffic, secrets masked). The header records the UI language to help with bug reports.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var language = (Loc.Culture ?? CultureInfo.CurrentUICulture).Name;
        TvLog.AttachFile(AppPaths.Log, $"FrameGallery {version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}, UI language {language}");

        // Unhandled exceptions go to the log instead of silently killing the application.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            TvLog.Write($"[CRASH] exception on the UI thread: {e.Exception}");
            MessageBox.Show(Loc.Get("Ui_UnexpectedError", e.Exception.Message), Ui.AppTitle);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => TvLog.Write($"[CRASH] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { TvLog.Write($"[CRASH] unobserved task exception: {e.Exception}"); e.SetObserved(); };

        Application.Run(new MainForm());
    }
}
