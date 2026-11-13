using FrameGallery.Desktop;

namespace FrameGallery.Tests;

public class FileNamesTests
{
    [Fact]
    public void A_short_name_is_left_alone() => Assert.Equal("photo.jpg", FileNames.Shorten("photo.jpg"));

    [Fact]
    public void A_long_name_is_cut_and_keeps_its_extension()
    {
        var shortened = FileNames.Shorten("holiday-tree-wallpaper-3840x2160-simple-christmas-design-modern-christmas-tree-28850.jpg");

        Assert.Equal(36, shortened.Length);
        Assert.StartsWith("holiday-tree-wallpaper-3840x", shortened);
        Assert.EndsWith("....jpg", shortened);
    }

    [Fact]
    public void The_example_from_the_request()
    {
        Assert.Equal("abcdef....jpg", FileNames.Shorten("abcdefghijklmnopqrstuvwxyz.jpg", 13));
    }

    [Fact]
    public void A_name_without_an_extension_gets_only_the_dots()
    {
        Assert.Equal("abcdefghij...", FileNames.Shorten("abcdefghijklmnopqrstuvwxyz", 13));
    }

    [Fact]
    public void A_huge_extension_cannot_make_it_longer_than_needed()
    {
        var result = FileNames.Shorten("a.extremely-long-extension-name-here", 10);

        Assert.StartsWith("a", result);
        Assert.Contains("...", result);
    }
}
