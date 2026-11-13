using System.Buffers.Binary;
using System.Text;
using FrameGallery.Core;

namespace FrameGallery.Tests;

/// <summary>The thumbnail frames of the data socket: what happens when the TV sends fewer than it announced.</summary>
public class ThumbnailStreamTests
{
    static byte[] Frame(string fileId, int total, byte[] data)
    {
        var header = Encoding.UTF8.GetBytes($$"""{"fileID":"{{fileId}}","num":"0","total":"{{total}}","fileLength":"{{data.Length}}"}""");
        var frame = new byte[4 + header.Length + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, header.Length);
        header.CopyTo(frame, 4);
        data.CopyTo(frame, 4 + header.Length);
        return frame;
    }

    static MemoryStream Stream(params byte[][] frames) => new(frames.SelectMany(f => f).ToArray());

    static readonly string[] Ids = ["A", "B", "C", "D"];

    [Fact]
    public async Task Every_announced_frame_is_read()
    {
        using var s = Stream(Frame("A", 4, [1]), Frame("B", 4, [2]), Frame("C", 4, [3]), Frame("D", 4, [4]));

        var result = await ArtClient.ReadThumbnailsAsync(s, Ids, default);

        Assert.Equal(["A", "B", "C", "D"], result.Keys);
        Assert.Equal(new byte[] { 3 }, result["C"]);
    }

    [Fact]
    public async Task A_stream_that_ends_early_keeps_the_frames_that_arrived()
    {
        // The TV announced 4 frames (the Art Store has no thumbnail for the rest) and closed the socket after 2.
        using var s = Stream(Frame("A", 4, [1]), Frame("B", 4, [2]));

        var result = await ArtClient.ReadThumbnailsAsync(s, Ids, default);

        Assert.Equal(["A", "B"], result.Keys);
    }

    [Fact]
    public async Task A_frame_cut_in_the_middle_keeps_the_frames_before_it()
    {
        var cut = Frame("B", 4, [2, 2, 2, 2]);
        using var s = Stream(Frame("A", 4, [1]), cut[..^2]);

        var result = await ArtClient.ReadThumbnailsAsync(s, Ids, default);

        Assert.Equal(["A"], result.Keys);
    }

    [Fact]
    public async Task A_stream_with_no_frame_at_all_is_still_an_error()
    {
        using var s = new MemoryStream();

        await Assert.ThrowsAsync<EndOfStreamException>(() => ArtClient.ReadThumbnailsAsync(s, Ids, default));
    }

    [Fact]
    public async Task An_identifier_the_TV_made_up_is_assigned_by_the_order_of_the_request()
    {
        using var s = Stream(Frame("x1", 2, [1]), Frame("x2", 2, [2]));

        var result = await ArtClient.ReadThumbnailsAsync(s, ["A", "B"], default);

        Assert.Equal(new byte[] { 2 }, result["B"]);
    }
}
