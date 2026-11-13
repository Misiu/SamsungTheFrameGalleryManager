namespace FrameGallery.Desktop;

static class FileNames
{
    /// <summary>
    /// Cuts a long file name so it fits in a title or a status line, keeping the extension:
    /// "holiday-tree-wallpaper-3840x2160-simple-christmas.jpg" becomes "holiday-tree-wallpaper-3840x2....jpg".
    /// </summary>
    public static string Shorten(string name, int max = 36)
    {
        if (name.Length <= max) return name;
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        var keep = Math.Max(1, max - extension.Length - 3);
        return stem[..Math.Min(keep, stem.Length)] + "..." + extension;
    }
}
