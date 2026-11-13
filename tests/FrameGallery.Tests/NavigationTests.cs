using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Tests;

/// <summary>The single window: a stack of screens, Back, and the crop screen handing its result to whoever is waiting for it.</summary>
public class NavigationTests
{
    sealed class FirstScreen : ScreenViewModel
    {
        public override bool HasBack => false;
    }

    sealed class Screen(string title, bool canLeave = true) : ScreenViewModel
    {
        public bool CanLeaveNow = canLeave;
        public bool Left;
        public override string Title => title;
        public override bool CanLeave => CanLeaveNow;
        public override void OnLeaving() => Left = true;
    }

    static ShellViewModel NewShell(out FirstScreen first)
    {
        var shell = new ShellViewModel("App");
        first = new FirstScreen();
        _ = shell.ShowAsync(first);
        return shell;
    }

    [Fact]
    public void The_title_follows_the_current_screen()
    {
        var shell = NewShell(out _);
        Assert.Equal("App", shell.Title);

        _ = shell.ShowAsync(new Screen("65\" The Frame"));
        Assert.Equal("App › 65\" The Frame", shell.Title);

        shell.GoBack();
        Assert.Equal("App", shell.Title);
    }

    [Fact]
    public async Task Back_returns_to_the_previous_screen_and_completes_the_task()
    {
        var shell = NewShell(out var first);
        var gallery = new Screen("TV");
        var shown = shell.ShowAsync(gallery);
        Assert.Same(gallery, shell.Current);
        Assert.False(shown.IsCompleted);

        shell.GoBack();

        Assert.Same(first, shell.Current);
        Assert.True(gallery.Left);
        await shown;
    }

    [Fact]
    public void Back_on_the_first_screen_does_nothing()
    {
        var shell = NewShell(out var first);

        shell.GoBack();

        Assert.Same(first, shell.Current);
    }

    [Fact]
    public void A_screen_that_cannot_be_left_stays_until_it_can()
    {
        var shell = NewShell(out _);
        var busy = new Screen("TV", canLeave: false);
        _ = shell.ShowAsync(busy);

        shell.GoBack();
        Assert.Same(busy, shell.Current);
        Assert.False(busy.Left);

        busy.CanLeaveNow = true;
        shell.GoBack();
        Assert.NotSame(busy, shell.Current);
        Assert.True(busy.Left);
    }

    [Fact]
    public void Only_the_top_screen_can_close()
    {
        var shell = NewShell(out _);
        var gallery = new Screen("TV");
        var crop = new Screen("crop");
        _ = shell.ShowAsync(gallery);
        _ = shell.ShowAsync(crop);

        gallery.GoBackCommand.Execute(null);   // not on top: ignored

        Assert.Same(crop, shell.Current);
        Assert.False(gallery.Left);
    }

    [Fact]
    public async Task The_crop_screen_hands_over_the_jpeg_and_closes()
    {
        var shell = NewShell(out var first);
        var crop = new CropViewModel("photo.jpg", 3840, 2160);
        var shown = shell.ShowAsync(crop);

        crop.Complete([1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, await crop.Result);
        await shown;
        Assert.Same(first, shell.Current);
    }

    [Fact]
    public async Task Leaving_the_crop_screen_by_Back_skips_the_photo()
    {
        var shell = NewShell(out _);
        var crop = new CropViewModel("photo.jpg", 3840, 2160);
        _ = shell.ShowAsync(crop);

        shell.GoBack();

        Assert.Null(await crop.Result);
    }

    [Fact]
    public async Task A_photo_that_cannot_be_opened_faults_the_result_and_closes_the_screen()
    {
        var shell = NewShell(out var first);
        var crop = new CropViewModel("broken.jpg", 3840, 2160);
        _ = shell.ShowAsync(crop);

        crop.Fail(new InvalidOperationException("not an image"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => crop.Result);
        Assert.Same(first, shell.Current);
    }

    [Fact]
    public void The_crop_screen_cannot_be_left_while_it_encodes()
    {
        var shell = NewShell(out _);
        var crop = new CropViewModel("photo.jpg", 3840, 2160) { IsEncoding = true };
        _ = shell.ShowAsync(crop);

        shell.GoBack();

        Assert.Same(crop, shell.Current);
    }

    [Fact]
    public void The_gallery_cannot_be_left_during_an_upload_but_leaving_cancels_it()
    {
        var shell = NewShell(out var first);
        var gallery = new GalleryViewModel(new TvRecord { Name = "TV" }, new GalleryViewModelTests.FakeArt(), new GalleryViewModelTests.FakeUi());
        _ = shell.ShowAsync(gallery);

        gallery.IsUploading = true;
        Assert.False(gallery.CanLeave);
        shell.GoBack();
        Assert.Same(gallery, shell.Current);

        gallery.IsUploading = false;
        Assert.True(gallery.CanLeave);
        shell.GoBack();
        Assert.Same(first, shell.Current);
    }
}
