using System.Reflection;
using System.Runtime.InteropServices;
using FrameGallery.Core;

namespace FrameGallery.App;

static class AppPaths
{
    /// <summary>Dane użytkownika (lista TV, zaszyfrowane tokeny): %APPDATA%\FrameGallery.</summary>
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameGallery");

    /// <summary>Katalog logów: podfolder „logs” w folderze aplikacji (łatwo go znaleźć i dołączyć do zgłoszenia błędu).</summary>
    public static string LogDir { get; } = ResolveLogDir();
    public static string Log => Path.Combine(LogDir, "log.txt");

    /// <summary>Gdy folder aplikacji jest tylko do odczytu (np. Program Files), logi trafiają do %LOCALAPPDATA%\FrameGallery\logs.</summary>
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

        // Log do pliku %APPDATA%\FrameGallery\log.txt (surowy ruch protokołu, sekrety zamaskowane).
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TvLog.AttachFile(AppPaths.Log, $"FrameGallery {version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}");

        // Nieobsłużone wyjątki trafiają do logu zamiast po cichu zabijać aplikację.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            TvLog.Write($"[CRASH] wyjątek w wątku UI: {e.Exception}");
            MessageBox.Show($"Nieoczekiwany błąd: {e.Exception.Message}\nSzczegóły w logu: {AppPaths.Log}", "Frame Gallery");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => TvLog.Write($"[CRASH] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { TvLog.Write($"[CRASH] zadanie bez obsługi błędu: {e.Exception}"); e.SetObserved(); };

        Application.Run(new MainForm());
    }
}
