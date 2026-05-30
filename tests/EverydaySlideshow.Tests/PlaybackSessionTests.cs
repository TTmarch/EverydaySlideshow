using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

public sealed class PlaybackSessionTests
{
    [Fact]
    public void PlaybackSession_supports_next_previous_loop_first_and_resume()
    {
        var items = new[]
        {
            TestData.Media(@"C:\photos\1.jpg"),
            TestData.Media(@"C:\photos\2.jpg"),
            TestData.Media(@"C:\photos\3.jpg")
        };

        var session = new PlaybackSession(items, loop: true);

        Assert.Equal("1.jpg", session.Current!.FileName);
        Assert.Equal("2.jpg", session.MoveNext()!.FileName);
        Assert.Equal("1.jpg", session.MovePrevious()!.FileName);
        Assert.Equal("3.jpg", session.MovePrevious()!.FileName);
        Assert.True(session.ResumeFromPath(@"C:\photos\2.jpg"));
        Assert.Equal("2.jpg", session.Current!.FileName);
        Assert.Equal("1.jpg", session.MoveFirst()!.FileName);
    }

    [Fact]
    public void PlaybackSession_without_loop_stops_at_end()
    {
        var items = new[]
        {
            TestData.Media(@"C:\photos\1.jpg"),
            TestData.Media(@"C:\photos\2.jpg")
        };

        var session = new PlaybackSession(items, loop: false);

        Assert.Equal("2.jpg", session.MoveNext()!.FileName);
        Assert.Null(session.MoveNext());
        Assert.Equal("2.jpg", session.Current!.FileName);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(999999, 86400)]
    public void PlaybackSpeedCatalog_normalizes_custom_seconds(int input, int expected)
    {
        Assert.Equal(expected, PlaybackSpeedCatalog.Normalize(input));
    }
}
