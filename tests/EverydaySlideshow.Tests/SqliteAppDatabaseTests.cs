using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

public sealed class SqliteAppDatabaseTests
{
    [Fact]
    public async Task Database_persists_folders_media_tags_history_settings_and_shuffle_state()
    {
        using var temp = new TempDirectory();
        var db = new SqliteAppDatabase(System.IO.Path.Combine(temp.Root, "test.db"));
        await db.InitializeAsync();

        var folder = new FolderProfile
        {
            Id = "folder-1",
            Name = "家族写真",
            Path = temp.Root,
            IncludeSubfolders = true,
            IsPrivate = false
        };
        await db.UpsertFolderAsync(folder);

        var mediaPath = temp.CreateFile("photo.jpg");
        await db.UpsertMediaItemsAsync([TestData.Media(mediaPath, folderId: folder.Id, folderName: "family")]);
        await db.SetMediaFlagAsync(mediaPath, nameof(MediaItem.IsFavorite), true);
        await db.SetMediaFlagAsync(mediaPath, nameof(MediaItem.IsWatchLater), true);
        await db.SetMediaFlagAsync(mediaPath, nameof(MediaItem.IsHidden), true);
        await db.SetMediaFlagAsync(mediaPath, nameof(MediaItem.IsHidden), false);
        await db.RecordPlaybackAsync(mediaPath, folder.Id, completed: false);

        var settings = new PlaybackSettings { SlideSeconds = 30, Loop = false, Order = PlaybackOrder.Sequential };
        await db.SaveSettingAsync("playback", settings);
        await db.SaveShuffleStateAsync(new ShuffleState
        {
            QueueKey = "all",
            RemainingPaths = ["a", "b"],
            RecentPaths = [mediaPath]
        });

        var folders = await db.GetFoldersAsync();
        var items = await db.GetMediaItemsAsync();
        var loadedSettings = await db.LoadSettingAsync("playback", new PlaybackSettings());
        var shuffle = await db.LoadShuffleStateAsync("all");

        Assert.Single(folders);
        Assert.Single(items);
        Assert.True(items[0].IsFavorite);
        Assert.True(items[0].IsWatchLater);
        Assert.False(items[0].IsHidden);
        Assert.NotNull(items[0].LastViewedUtc);
        Assert.Equal(1, items[0].ViewCount);
        Assert.Equal(30, loadedSettings.SlideSeconds);
        Assert.False(loadedSettings.Loop);
        Assert.Equal(["a", "b"], shuffle!.RemainingPaths);
        Assert.Equal(1, await db.CountMediaItemsAsync());
    }

    [Fact]
    public async Task Database_persists_playlists_and_removes_deleted_folders_from_them()
    {
        using var temp = new TempDirectory();
        var db = new SqliteAppDatabase(System.IO.Path.Combine(temp.Root, "playlist.db"));
        await db.InitializeAsync();

        var family = new FolderProfile { Id = "family", Name = "家族", Path = temp.Root };
        var travel = new FolderProfile { Id = "travel", Name = "旅行", Path = temp.Root };
        await db.UpsertFolderAsync(family);
        await db.UpsertFolderAsync(travel);

        await db.UpsertPlaylistAsync(new FolderPlaylist
        {
            Id = "mixed",
            Name = "家族と旅行",
            FolderIds = [family.Id, travel.Id]
        });

        var playlists = await db.GetPlaylistsAsync();
        Assert.Single(playlists);
        Assert.Equal(["family", "travel"], playlists[0].FolderIds);

        await db.DeleteFolderAsync(family.Id);
        playlists = await db.GetPlaylistsAsync();

        Assert.Single(playlists);
        Assert.Equal(["travel"], playlists[0].FolderIds);
    }

    [Fact]
    public async Task Settings_persist_window_and_custom_mood_profiles()
    {
        using var temp = new TempDirectory();
        var db = new SqliteAppDatabase(System.IO.Path.Combine(temp.Root, "settings.db"));
        await db.InitializeAsync();

        await db.SaveSettingAsync("display", new DisplaySettings
        {
            WindowLeft = 20,
            WindowTop = 30,
            WindowWidth = 900,
            WindowHeight = 620,
            WindowMaximized = true,
            DarkMode = true
        });
        await db.SaveSettingAsync("moods", new List<CustomMoodProfile>
        {
            new()
            {
                Name = "ゆっくり",
                SlideSeconds = 45,
                Opacity = 0.7,
                MuteVideo = true,
                VerticalBias = VerticalPhotoBias.Prefer
            }
        });

        var display = await db.LoadSettingAsync("display", new DisplaySettings());
        var moods = await db.LoadSettingAsync("moods", new List<CustomMoodProfile>());

        Assert.Equal(900, display.WindowWidth);
        Assert.True(display.WindowMaximized);
        Assert.True(display.DarkMode);
        Assert.Single(moods);
        Assert.Equal("ゆっくり", moods[0].Name);
        Assert.Equal(VerticalPhotoBias.Prefer, moods[0].VerticalBias);
    }

    [Fact]
    public async Task Database_clears_hidden_and_deletion_candidate_flags_in_bulk()
    {
        using var temp = new TempDirectory();
        var db = new SqliteAppDatabase(System.IO.Path.Combine(temp.Root, "reset-tags.db"));
        await db.InitializeAsync();

        var folder = new FolderProfile { Id = "folder", Name = "写真", Path = temp.Root };
        await db.UpsertFolderAsync(folder);

        var firstPath = temp.CreateFile("first.jpg");
        var secondPath = temp.CreateFile("second.jpg");
        await db.UpsertMediaItemsAsync([
            TestData.Media(firstPath, folderId: folder.Id, folderName: folder.Name),
            TestData.Media(secondPath, folderId: folder.Id, folderName: folder.Name)
        ]);
        await db.SetMediaFlagAsync(firstPath, nameof(MediaItem.IsHidden), true);
        await db.SetMediaFlagAsync(firstPath, nameof(MediaItem.IsDeletionCandidate), true);
        await db.SetMediaFlagAsync(secondPath, nameof(MediaItem.IsHidden), true);

        var hiddenCleared = await db.ClearMediaFlagAsync(nameof(MediaItem.IsHidden));
        var items = await db.GetMediaItemsAsync();

        Assert.Equal(2, hiddenCleared);
        Assert.All(items, item => Assert.False(item.IsHidden));
        Assert.Contains(items, item => item.Path == firstPath && item.IsDeletionCandidate);

        var deletionCleared = await db.ClearMediaFlagAsync(nameof(MediaItem.IsDeletionCandidate));
        items = await db.GetMediaItemsAsync();

        Assert.Equal(1, deletionCleared);
        Assert.All(items, item => Assert.False(item.IsDeletionCandidate));
    }
}
