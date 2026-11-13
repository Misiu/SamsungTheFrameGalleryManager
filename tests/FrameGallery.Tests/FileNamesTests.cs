using FrameGallery.Desktop;

namespace FrameGallery.Tests;

/// <summary>The name is cut only as far as the room requires; "room" here is a number of characters.</summary>
public class FileNamesTests
{
    static Func<string, bool> Room(int characters) => text => text.Length <= characters;

    [Fact]
    public void A_name_that_fits_is_left_whole()
    {
        Assert.Equal("photo.jpg", FileNames.Fit("{0}", "photo.jpg", Room(9)));
        Assert.Equal("photo.jpg", FileNames.Fit("Crop: {0}", "photo.jpg", Room(100)));
    }

    [Fact]
    public void One_character_too_many_cuts_just_enough_and_keeps_the_extension()
    {
        // "abcdefghij.jpg" is 14 characters; with room for 13 the name part gives up three letters and gets the dots.
        var result = FileNames.Fit("{0}", "abcdefghij.jpg", Room(13));

        Assert.True(result.Length <= 13);
        Assert.EndsWith("....jpg", result);
        Assert.StartsWith("abcdef", result);
    }

    [Fact]
    public void The_text_around_the_name_takes_room_too()
    {
        var result = FileNames.Fit("Crop: {0}", "abcdefghijklmnopqrstuvwxyz.jpg", Room(20));

        Assert.True(("Crop: " + result).Length <= 20);
        Assert.EndsWith("....jpg", result);
    }

    [Fact]
    public void The_example_from_the_request()
    {
        Assert.Equal("abcdef....jpg", FileNames.Fit("{0}", "abcdefghijklmnopqrstuvwxyz.jpg", Room(13)));
    }

    [Fact]
    public void A_name_without_an_extension_gets_only_the_dots()
    {
        Assert.Equal("abcdefghij...", FileNames.Fit("{0}", "abcdefghijklmnopqrstuvwxyz", Room(13)));
    }

    [Fact]
    public void When_nothing_fits_the_dots_and_the_extension_are_left()
    {
        Assert.Equal("....jpg", FileNames.Fit("{0}", "abcdefghijklmnopqrstuvwxyz.jpg", Room(3)));
    }

    [Fact]
    public void The_cut_follows_the_room_one_character_at_a_time()
    {
        var name = "abcdefghijklmnopqrstuvwxyz.jpg";
        var lengths = Enumerable.Range(7, 20).Select(room => FileNames.Fit("{0}", name, Room(room)).Length).ToList();

        Assert.Equal(lengths.Order(), lengths);   // more room never gives a shorter name
        Assert.All(lengths.Zip(Enumerable.Range(7, 20)), pair => Assert.True(pair.First <= Math.Max(pair.Second, 7)));
    }
}
