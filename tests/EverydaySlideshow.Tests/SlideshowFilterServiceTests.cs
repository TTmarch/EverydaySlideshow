using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

public sealed class SlideshowFilterServiceTests
{
    [Fact]
    public void Apply_removes_hidden_delete_candidates_screenshots_and_videos()
    {
        var now = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        var items = new[]
        {
            TestData.Media(@"C:\photos\keep.jpg", favorite: true, modifiedUtc: now.AddDays(-80)),
            TestData.Media(@"C:\photos\hidden.jpg", hidden: true),
            TestData.Media(@"C:\photos\delete.jpg", deletionCandidate: true),
            TestData.Media(@"C:\photos\Screenshot_01.png"),
            TestData.Media(@"C:\photos\clip.mp4", kind: MediaKind.Video)
        };

        var filtered = SlideshowFilterService.Apply(items, new SlideshowFilterOptions
        {
            IncludeVideos = false,
            ExcludeScreenshots = true,
            Now = now
        });

        Assert.Single(filtered);
        Assert.Equal("keep.jpg", filtered[0].FileName);
    }

    [Fact]
    public void Apply_supports_favorites_recently_unseen_and_anniversary_filters()
    {
        var now = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        var items = new[]
        {
            TestData.Media(@"C:\photos\old-favorite.jpg", favorite: true, capturedUtc: new DateTimeOffset(2020, 5, 28, 12, 0, 0, TimeSpan.Zero), lastViewedUtc: now.AddDays(-100)),
            TestData.Media(@"C:\photos\recent-favorite.jpg", favorite: true, capturedUtc: new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), lastViewedUtc: now.AddDays(-1)),
            TestData.Media(@"C:\photos\plain.jpg", favorite: false, capturedUtc: new DateTimeOffset(2020, 5, 29, 12, 0, 0, TimeSpan.Zero), lastViewedUtc: null)
        };

        var filtered = SlideshowFilterService.Apply(items, new SlideshowFilterOptions
        {
            FavoritesOnly = true,
            RecentlyUnseenOnly = true,
            AnniversaryAroundTodayOnly = true,
            RecentlyUnseenDays = 60,
            AnniversaryWindowDays = 3,
            ExcludeScreenshots = false,
            Now = now
        });

        Assert.Single(filtered);
        Assert.Equal("old-favorite.jpg", filtered[0].FileName);
    }

    [Fact]
    public void Apply_family_safe_mode_avoids_private_and_watch_later_items()
    {
        var items = new[]
        {
            TestData.Media(@"C:\photos\family.jpg"),
            TestData.Media(@"C:\photos\private.jpg", privateFolder: true),
            TestData.Media(@"C:\photos\later.jpg", watchLater: true)
        };

        var filtered = SlideshowFilterService.Apply(items, new SlideshowFilterOptions
        {
            FamilySafeMode = true,
            ExcludeScreenshots = false
        });

        Assert.Single(filtered);
        Assert.Equal("family.jpg", filtered[0].FileName);
    }

    [Fact]
    public void Apply_supports_watch_later_only_filter()
    {
        var items = new[]
        {
            TestData.Media(@"C:\photos\later.jpg", watchLater: true),
            TestData.Media(@"C:\photos\normal.jpg"),
            TestData.Media(@"C:\photos\hidden-later.jpg", watchLater: true, hidden: true)
        };

        var filtered = SlideshowFilterService.Apply(items, new SlideshowFilterOptions
        {
            WatchLaterOnly = true,
            ExcludeScreenshots = false
        });

        Assert.Single(filtered);
        Assert.Equal("later.jpg", filtered[0].FileName);
    }

    [Fact]
    public void Apply_watch_later_only_is_not_blocked_by_family_safe_watch_later_rule()
    {
        var items = new[]
        {
            TestData.Media(@"C:\photos\later.jpg", watchLater: true),
            TestData.Media(@"C:\photos\normal.jpg")
        };

        var filtered = SlideshowFilterService.Apply(items, new SlideshowFilterOptions
        {
            WatchLaterOnly = true,
            FamilySafeMode = true,
            ExcludeScreenshots = false
        });

        Assert.Single(filtered);
        Assert.Equal("later.jpg", filtered[0].FileName);
    }
}
