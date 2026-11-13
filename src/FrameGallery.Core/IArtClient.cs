namespace FrameGallery.Core;

/// <summary>
/// The operations on the TV's art gallery. <see cref="ArtClient"/> talks to a real TV; the desktop app's demo mode
/// implements it with fake data, so the interface can be shown and tested without a TV.
/// </summary>
public interface IArtClient : IAsyncDisposable
{
    Task<IReadOnlyList<ArtItem>> ListAsync(CancellationToken ct);

    /// <returns>Thumbnails (JPEG) by content_id; missing ones are skipped.</returns>
    Task<IReadOnlyDictionary<string, byte[]>> GetThumbnailsAsync(IReadOnlyList<string> contentIds, CancellationToken ct);

    Task SelectImageAsync(string contentId, bool show, CancellationToken ct);

    Task<string?> GetCurrentArtworkAsync(CancellationToken ct);

    Task DeleteAsync(IReadOnlyList<string> contentIds, CancellationToken ct);

    /// <param name="progress">Receives the fraction (0..1) of the file already sent.</param>
    /// <returns>The content_id of the new image.</returns>
    Task<string> UploadAsync(byte[] jpeg, CancellationToken ct, IProgress<double>? progress = null);
}
