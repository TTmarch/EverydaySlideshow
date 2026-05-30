namespace EverydaySlideshow.Core;

public sealed class PlaybackSession
{
    private readonly List<MediaItem> _items;

    public PlaybackSession(IEnumerable<MediaItem> items, bool loop = true)
    {
        _items = items.ToList();
        Loop = loop;
        CurrentIndex = _items.Count > 0 ? 0 : -1;
    }

    public bool Loop { get; set; }
    public bool IsPaused { get; private set; }
    public int CurrentIndex { get; private set; }
    public MediaItem? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;
    public IReadOnlyList<MediaItem> Items => _items;

    public void SetPaused(bool paused) => IsPaused = paused;

    public void TogglePaused() => IsPaused = !IsPaused;

    public MediaItem? MoveFirst()
    {
        CurrentIndex = _items.Count > 0 ? 0 : -1;
        return Current;
    }

    public MediaItem? MoveNext()
    {
        if (_items.Count == 0)
        {
            CurrentIndex = -1;
            return null;
        }

        if (CurrentIndex < _items.Count - 1)
        {
            CurrentIndex++;
            return Current;
        }

        if (!Loop)
        {
            return null;
        }

        CurrentIndex = 0;
        return Current;
    }

    public MediaItem? MovePrevious()
    {
        if (_items.Count == 0)
        {
            CurrentIndex = -1;
            return null;
        }

        if (CurrentIndex > 0)
        {
            CurrentIndex--;
            return Current;
        }

        CurrentIndex = Loop ? _items.Count - 1 : 0;
        return Current;
    }

    public bool ResumeFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var index = _items.FindIndex(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        CurrentIndex = index;
        return true;
    }
}

public static class PlaybackSpeedCatalog
{
    public static readonly int[] PresetSeconds = [1, 2, 3, 5, 10, 30, 60, 300];

    public static int Normalize(int requestedSeconds)
        => Math.Clamp(requestedSeconds, 1, 24 * 60 * 60);

    public static bool IsPreset(int seconds)
        => PresetSeconds.Contains(seconds);
}
