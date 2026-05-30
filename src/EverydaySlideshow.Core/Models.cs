namespace EverydaySlideshow.Core;

public enum MediaKind
{
    Image = 0,
    Video = 1
}

public enum PlaybackOrder
{
    SmartShuffle = 0,
    Sequential = 1
}

public enum DisplayModeKind
{
    Window = 0,
    Fullscreen = 1,
    Borderless = 2
}

public enum FitMode
{
    Fit = 0,
    Fill = 1,
    Original = 2,
    BlurBackground = 3
}

public enum MoodMode
{
    Normal = 0,
    Work = 1,
    Bedtime = 2,
    FamilySafe = 3,
    Custom = 4
}

public enum VerticalPhotoBias
{
    Normal = 0,
    Prefer = 1,
    Avoid = 2
}

public enum AppLanguage
{
    English = 0,
    Japanese = 1
}

public sealed class FolderProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public bool IsPrivate { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedUtc { get; set; }
}

public sealed class MediaItem
{
    public string Id { get; set; } = "";
    public string FolderId { get; set; } = "";
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    public string FolderName { get; set; } = "";
    public MediaKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public DateTimeOffset? CapturedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastViewedUtc { get; set; }
    public int ViewCount { get; set; }
    public bool IsMissing { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeletionCandidate { get; set; }
    public bool IsWatchLater { get; set; }
    public bool IsFolderExcluded { get; set; }
    public bool IsFromPrivateFolder { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    public bool IsVideo => Kind == MediaKind.Video;
    public bool IsImage => Kind == MediaKind.Image;
    public bool IsVertical => Width.HasValue && Height.HasValue && Height.Value > Width.Value;
    public DateTimeOffset SortDate => CapturedUtc ?? ModifiedUtc;
}

public sealed class SlideshowFilterOptions
{
    public bool FavoritesOnly { get; set; }
    public bool WatchLaterOnly { get; set; }
    public bool RecentlyUnseenOnly { get; set; }
    public bool AnniversaryAroundTodayOnly { get; set; }
    public bool ExcludeScreenshots { get; set; } = true;
    public bool IncludeVideos { get; set; } = true;
    public bool FamilySafeMode { get; set; }
    public VerticalPhotoBias VerticalBias { get; set; } = VerticalPhotoBias.Normal;
    public int RecentlyUnseenDays { get; set; } = 60;
    public int AnniversaryWindowDays { get; set; } = 7;
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;
}

public sealed class PlaybackSettings
{
    public PlaybackOrder Order { get; set; } = PlaybackOrder.SmartShuffle;
    public bool Loop { get; set; } = true;
    public int SlideSeconds { get; set; } = 5;
    public int CustomSlideSeconds { get; set; } = 15;
    public bool ResumeLastPosition { get; set; } = true;
    public bool PauseEachPhoto { get; set; }
    public MoodMode Mood { get; set; } = MoodMode.Normal;
    public string? CustomMoodProfileId { get; set; }
}

public sealed class DisplaySettings
{
    public DisplayModeKind DisplayMode { get; set; } = DisplayModeKind.Window;
    public FitMode FitMode { get; set; } = FitMode.BlurBackground;
    public bool Topmost { get; set; }
    public double Opacity { get; set; } = 1.0;
    public string? MonitorDeviceName { get; set; }
    public bool AutoStartWithWindows { get; set; }
    public bool IdleAutoPlay { get; set; }
    public int IdleAutoPlayMinutes { get; set; } = 10;
    public bool ResumeAfterWake { get; set; } = true;
    public bool StartSlideshowFullscreen { get; set; }
    public bool DarkMode { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public bool MuteVideo { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1240;
    public double WindowHeight { get; set; } = 760;
    public bool WindowMaximized { get; set; }
}

public sealed class CustomMoodProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom";
    public int SlideSeconds { get; set; } = 15;
    public double Opacity { get; set; } = 1.0;
    public bool MuteVideo { get; set; }
    public bool IncludeVideos { get; set; } = true;
    public bool ExcludeScreenshots { get; set; } = true;
    public bool FamilySafeMode { get; set; }
    public VerticalPhotoBias VerticalBias { get; set; } = VerticalPhotoBias.Normal;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FolderPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> FolderIds { get; set; } = [];
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public int FolderCount => FolderIds.Count;
}

public sealed class PlaybackResumeState
{
    public string? LastFolderId { get; set; }
    public string? LastMediaPath { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ShuffleState
{
    public string QueueKey { get; set; } = "";
    public List<string> RemainingPaths { get; set; } = [];
    public List<string> RecentPaths { get; set; } = [];
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SmartShuffleOptions
{
    public int RecentWindow { get; set; } = 25;
    public double FavoriteBoost { get; set; } = 0.35;
    public bool PreferOlderUnseen { get; set; } = true;
    public VerticalPhotoBias VerticalBias { get; set; } = VerticalPhotoBias.Normal;
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record SmartShufflePick(MediaItem? Item, ShuffleState State);

public sealed record ScanProgress(string FolderId, int FoundCount, string? CurrentPath);
