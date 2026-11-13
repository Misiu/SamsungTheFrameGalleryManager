namespace FrameGallery.Desktop;

static class FileNames
{
    /// <summary>
    /// Cuts the name only as far as needed: the full name when <c>format</c> with it fits, otherwise the end of the name part is
    /// replaced with dots and the extension is kept ("holiday-tree-wallpaper-38....jpg"). <paramref name="fits"/> says whether a
    /// finished text fits the room there is.
    /// </summary>
    /// <param name="format">The text around the name, with {0} for the name, e.g. "Crop: {0}".</param>
    public static string Fit(string format, string name, Func<string, bool> fits)
    {
        if (fits(string.Format(format, name))) return name;

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];

        // The longest piece of the name part that still fits (the text only gets wider with more characters).
        int low = 0, high = stem.Length - 1, best = 0;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (fits(string.Format(format, stem[..middle] + "..." + extension))) { best = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return stem[..best] + "..." + extension;
    }
}
