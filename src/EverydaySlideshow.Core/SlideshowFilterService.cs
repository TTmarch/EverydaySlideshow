namespace EverydaySlideshow.Core;

public static class SlideshowFilterService
{
    public static IReadOnlyList<MediaItem> Apply(IEnumerable<MediaItem> items, SlideshowFilterOptions options)
    {
        var query = items.Where(item =>
            !item.IsMissing
            && !item.IsHidden
            && !item.IsDeletionCandidate
            && !item.IsFolderExcluded
            && !string.IsNullOrWhiteSpace(item.Path));

        if (!options.IncludeVideos)
        {
            query = query.Where(item => item.Kind == MediaKind.Image);
        }

        if (options.FavoritesOnly)
        {
            query = query.Where(item => item.IsFavorite);
        }

        if (options.WatchLaterOnly)
        {
            query = query.Where(item => item.IsWatchLater);
        }

        if (options.FamilySafeMode)
        {
            query = query.Where(item =>
                !item.IsFromPrivateFolder
                && (options.WatchLaterOnly || !item.IsWatchLater));
        }

        if (options.RecentlyUnseenOnly)
        {
            var threshold = options.Now.AddDays(-Math.Max(1, options.RecentlyUnseenDays));
            query = query.Where(item => item.LastViewedUtc is null || item.LastViewedUtc < threshold);
        }

        if (options.AnniversaryAroundTodayOnly)
        {
            query = query.Where(item => IsNearAnniversary(item.SortDate, options.Now, options.AnniversaryWindowDays));
        }

        return query
            .OrderByDescending(item => BiasScore(item, options))
            .ThenBy(item => item.SortDate)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsNearAnniversary(DateTimeOffset date, DateTimeOffset now, int windowDays)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var candidate = new DateOnly(today.Year, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(today.Year, date.Month)));
        var delta = Math.Abs(candidate.DayNumber - today.DayNumber);
        var wrapped = Math.Min(delta, 366 - delta);
        return wrapped <= Math.Max(0, windowDays);
    }

    private static int BiasScore(MediaItem item, SlideshowFilterOptions options)
    {
        if (!item.IsImage)
        {
            return 0;
        }

        return options.VerticalBias switch
        {
            VerticalPhotoBias.Prefer when item.IsVertical => 1,
            VerticalPhotoBias.Avoid when item.IsVertical => -1,
            _ => 0
        };
    }
}
