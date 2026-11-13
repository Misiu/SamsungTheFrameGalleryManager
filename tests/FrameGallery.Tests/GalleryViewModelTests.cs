using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;

namespace FrameGallery.Tests;

/// <summary>After adding or deleting, the gallery updates its tiles itself and does not ask the TV for the list again.</summary>
public class GalleryViewModelTests
{
    internal sealed class FakeArt : IArtClient
    {
        public int Lists;
        public List<ArtItem> Items = Enumerable.Range(1, 3)
            .Select(i => new ArtItem($"MY_F000{i}", ArtClient.MyPhotosCategory, "none", 3840, 2160, "2026:01:01 10:00:00")).ToList();

        public Task<IReadOnlyList<ArtItem>> ListAsync(CancellationToken ct) { Lists++; return Task.FromResult<IReadOnlyList<ArtItem>>(Items.ToList()); }
        public Task<IReadOnlyDictionary<string, byte[]>> GetThumbnailsAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, byte[]>>(new Dictionary<string, byte[]>());   // no pictures: the tiles show the "failed" icon
        public List<(string Id, bool Show)> Selected = [];
        public Task SelectImageAsync(string contentId, bool show, CancellationToken ct) { Selected.Add((contentId, show)); return Task.CompletedTask; }
        public Task<string?> GetCurrentArtworkAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct) { Items.RemoveAll(i => ids.Contains(i.ContentId)); return Task.CompletedTask; }
        public Task<string> UploadAsync(byte[] jpeg, CancellationToken ct, IProgress<double>? progress = null) => Task.FromResult("MY_F0099");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FakeUi : IInteraction
    {
        public Task MessageAsync(string text) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string text) => Task.FromResult(true);
        public Task<string?> AskAsync(string title, string prompt) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickImagesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<byte[]?> CropAsync(string path, int w, int h) => Task.FromResult<byte[]?>([1, 2, 3]);
        public Task ShowGalleryAsync(TvRecord tv, IArtClient art) => Task.CompletedTask;
        public void OpenLog() { }
    }

    static async Task<(GalleryViewModel Vm, FakeArt Art)> OpenAsync()
    {
        var art = new FakeArt();
        var vm = new GalleryViewModel(new TvRecord { Name = "TV" }, art, new FakeUi());
        await vm.ReloadAsync();
        return (vm, art);
    }

    [Fact]
    public async Task Showing_a_picture_selects_it_on_the_TV_and_switches_to_Art_Mode()
    {
        var (vm, art) = await OpenAsync();

        await vm.ShowOnTvAsync(vm.Items[1]);

        Assert.Equal([("MY_F0002", true)], art.Selected);
        Assert.Equal(1, art.Lists);   // nothing to refetch
    }

    [Fact]
    public async Task Deleting_removes_the_tiles_without_listing_again()
    {
        var (vm, art) = await OpenAsync();

        await vm.DeleteAsync([vm.Items[0], vm.Items[2]]);

        Assert.Equal(["MY_F0002"], vm.Items.Select(i => i.Item.ContentId));
        Assert.Equal(1, art.Lists);
    }

    [Fact]
    public async Task Adding_puts_the_new_tile_first_without_listing_again()
    {
        var (vm, art) = await OpenAsync();

        await vm.AddFilesAsync(["photo.jpg"]);

        Assert.Equal(4, vm.Items.Count);
        Assert.Equal("MY_F0099", vm.Items[0].Item.ContentId);
        Assert.True(vm.Items[0].IsOwn);
        Assert.Equal(1, art.Lists);
    }
}
