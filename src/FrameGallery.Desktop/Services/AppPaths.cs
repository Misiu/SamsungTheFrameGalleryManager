namespace FrameGallery.Desktop;

static class AppPaths
{
    /// <summary>User data (the TV list, protected tokens): %APPDATA%\FrameGallery on Windows (shared with the WinForms prototype).</summary>
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameGallery");

    /// <summary>The log folder: a "logs" subfolder of the application folder (easy to find and to attach to a bug report).</summary>
    public static string LogDir { get; } = ResolveLogDir();
    public static string Log => Path.Combine(LogDir, "log.txt");

    /// <summary>If the application folder is read-only (e.g. Program Files), logs go to the local application data folder.</summary>
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
