using System.Text.RegularExpressions;

namespace EverydaySlideshow.Core;

public sealed class SmartShuffleEngine
{
    private readonly Random _random;

    public SmartShuffleEngine(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    public SmartShufflePick PickNext(
        IReadOnlyList<MediaItem> items,
        ShuffleState? previousState,
        SmartShuffleOptions? options = null,
        string queueKey = "default")
    {
        options ??= new SmartShuffleOptions();
        var available = items
            .Where(item => !item.IsMissing && File.Exists(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);

        if (available.Count == 0)
        {
            return new SmartShufflePick(null, new ShuffleState { QueueKey = queueKey });
        }

        var state = NormalizeState(previousState, available, queueKey, options.RecentWindow);
        if (state.RemainingPaths.Count == 0)
        {
            state.RemainingPaths = BuildQueue(available.Values.ToList(), state.RecentPaths, options);
        }

        while (state.RemainingPaths.Count > 0)
        {
            var nextPath = state.RemainingPaths[0];
            state.RemainingPaths.RemoveAt(0);
            if (!available.TryGetValue(nextPath, out var item))
            {
                continue;
            }

            state.RecentPaths.Insert(0, item.Path);
            state.RecentPaths = state.RecentPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, options.RecentWindow))
                .ToList();
            state.UpdatedUtc = DateTimeOffset.UtcNow;
            return new SmartShufflePick(item, state);
        }

        return PickNext(items, state, options, queueKey);
    }

    public List<string> BuildQueue(IReadOnlyList<MediaItem> items, IReadOnlyList<string>? recentPaths, SmartShuffleOptions? options = null)
    {
        options ??= new SmartShuffleOptions();
        var remaining = items
            .Where(item => !item.IsMissing && !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var recent = new HashSet<string>(recentPaths ?? [], StringComparer.OrdinalIgnoreCase);
        var ordered = new List<MediaItem>(remaining.Count);
        MediaItem? previous = null;

        while (remaining.Count > 0)
        {
            var selected = remaining
                .OrderByDescending(item => Score(item, previous, recent, options))
                .First();
            ordered.Add(selected);
            remaining.Remove(selected);
            previous = selected;
        }

        return ordered.Select(item => item.Path).ToList();
    }

    private ShuffleState NormalizeState(
        ShuffleState? state,
        IReadOnlyDictionary<string, MediaItem> available,
        string queueKey,
        int recentWindow)
    {
        if (state is null || !string.Equals(state.QueueKey, queueKey, StringComparison.Ordinal))
        {
            return new ShuffleState { QueueKey = queueKey };
        }

        state.RemainingPaths = state.RemainingPaths
            .Where(available.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        state.RecentPaths = state.RecentPaths
            .Where(available.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, recentWindow))
            .ToList();
        return state;
    }

    private double Score(MediaItem item, MediaItem? previous, HashSet<string> recentPaths, SmartShuffleOptions options)
    {
        var score = _random.NextDouble();

        if (item.IsFavorite)
        {
            score += options.FavoriteBoost;
        }

        if (options.PreferOlderUnseen)
        {
            score += item.LastViewedUtc is null
                ? 0.45
                : Math.Clamp((options.Now - item.LastViewedUtc.Value).TotalDays / 365.0, 0, 0.45);
        }

        if (recentPaths.Contains(item.Path))
        {
            score -= 1.2;
        }

        if (item.IsImage)
        {
            score += options.VerticalBias switch
            {
                VerticalPhotoBias.Prefer when item.IsVertical => 0.22,
                VerticalPhotoBias.Avoid when item.IsVertical => -0.22,
                _ => 0
            };
        }

        if (previous is not null)
        {
            if (string.Equals(previous.FolderName, item.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                score -= 0.75;
            }

            if (previous.SortDate.Date == item.SortDate.Date)
            {
                score -= 0.55;
            }

            if (SimilarNameKey(previous.FileName) == SimilarNameKey(item.FileName))
            {
                score -= 0.45;
            }
        }

        return score;
    }

    private static string SimilarNameKey(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        name = Regex.Replace(name, @"[\d_\-\s]+", "");
        return name.Length <= 8 ? name : name[..8];
    }
}
