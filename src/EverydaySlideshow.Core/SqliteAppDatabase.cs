using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace EverydaySlideshow.Core;

public sealed class SqliteAppDatabase
{
    private readonly string _dbPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqliteAppDatabase(string dbPath)
    {
        _dbPath = dbPath;
        var directory = System.IO.Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public Task InitializeAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS registered_folders (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                path TEXT NOT NULL,
                include_subfolders INTEGER NOT NULL,
                is_private INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                last_played_utc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS media_items (
                id TEXT PRIMARY KEY,
                folder_id TEXT NOT NULL,
                path TEXT NOT NULL UNIQUE,
                file_name TEXT NOT NULL,
                extension TEXT NOT NULL,
                folder_name TEXT NOT NULL,
                kind INTEGER NOT NULL,
                size_bytes INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                modified_utc TEXT NOT NULL,
                captured_utc TEXT NULL,
                last_seen_utc TEXT NOT NULL,
                last_viewed_utc TEXT NULL,
                view_count INTEGER NOT NULL DEFAULT 0,
                is_missing INTEGER NOT NULL DEFAULT 0,
                width INTEGER NULL,
                height INTEGER NULL,
                FOREIGN KEY(folder_id) REFERENCES registered_folders(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS media_tags (
                path TEXT PRIMARY KEY,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                is_hidden INTEGER NOT NULL DEFAULT 0,
                is_deletion_candidate INTEGER NOT NULL DEFAULT 0,
                is_watch_later INTEGER NOT NULL DEFAULT 0,
                is_folder_excluded INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS playback_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                folder_id TEXT NULL,
                viewed_utc TEXT NOT NULL,
                completed INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shuffle_states (
                queue_key TEXT PRIMARY KEY,
                state_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS playlists (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS playlist_folders (
                playlist_id TEXT NOT NULL,
                folder_id TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                PRIMARY KEY (playlist_id, folder_id),
                FOREIGN KEY(playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
                FOREIGN KEY(folder_id) REFERENCES registered_folders(id) ON DELETE CASCADE
            );
            """;
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FolderProfile>> GetFoldersAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, path, include_subfolders, is_private, is_enabled, created_utc, last_played_utc
            FROM registered_folders
            ORDER BY last_played_utc IS NULL, last_played_utc DESC, name COLLATE NOCASE
            """;
        using var reader = command.ExecuteReader();
        var folders = new List<FolderProfile>();
        while (reader.Read())
        {
            folders.Add(new FolderProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Path = reader.GetString(2),
                IncludeSubfolders = reader.GetInt32(3) != 0,
                IsPrivate = reader.GetInt32(4) != 0,
                IsEnabled = reader.GetInt32(5) != 0,
                CreatedUtc = ReadDate(reader, 6) ?? DateTimeOffset.UtcNow,
                LastPlayedUtc = ReadDate(reader, 7)
            });
        }

        return Task.FromResult<IReadOnlyList<FolderProfile>>(folders);
    }

    public Task UpsertFolderAsync(FolderProfile folder)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO registered_folders
                (id, name, path, include_subfolders, is_private, is_enabled, created_utc, last_played_utc)
            VALUES
                ($id, $name, $path, $include, $private, $enabled, $created, $lastPlayed)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                path = excluded.path,
                include_subfolders = excluded.include_subfolders,
                is_private = excluded.is_private,
                is_enabled = excluded.is_enabled,
                last_played_utc = excluded.last_played_utc
            """;
        command.Parameters.AddWithValue("$id", folder.Id);
        command.Parameters.AddWithValue("$name", folder.Name);
        command.Parameters.AddWithValue("$path", folder.Path);
        command.Parameters.AddWithValue("$include", folder.IncludeSubfolders ? 1 : 0);
        command.Parameters.AddWithValue("$private", folder.IsPrivate ? 1 : 0);
        command.Parameters.AddWithValue("$enabled", folder.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created", WriteDate(folder.CreatedUtc));
        command.Parameters.AddWithValue("$lastPlayed", WriteNullableDate(folder.LastPlayedUtc));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task TouchFolderPlayedAsync(string folderId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE registered_folders SET last_played_utc = $now WHERE id = $id";
        command.Parameters.AddWithValue("$now", WriteDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", folderId);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string folderId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM registered_folders WHERE id = $id";
        command.Parameters.AddWithValue("$id", folderId);
        command.ExecuteNonQuery();
        RemoveEmptyPlaylists(connection);
        return Task.CompletedTask;
    }

    public Task UpsertMediaItemsAsync(IEnumerable<MediaItem> items)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in items)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO media_items
                    (id, folder_id, path, file_name, extension, folder_name, kind, size_bytes,
                     created_utc, modified_utc, captured_utc, last_seen_utc, last_viewed_utc,
                     view_count, is_missing, width, height)
                VALUES
                    ($id, $folderId, $path, $fileName, $extension, $folderName, $kind, $size,
                     $created, $modified, $captured, $seen, $viewed, $viewCount, $missing, $width, $height)
                ON CONFLICT(path) DO UPDATE SET
                    id = excluded.id,
                    folder_id = excluded.folder_id,
                    file_name = excluded.file_name,
                    extension = excluded.extension,
                    folder_name = excluded.folder_name,
                    kind = excluded.kind,
                    size_bytes = excluded.size_bytes,
                    created_utc = excluded.created_utc,
                    modified_utc = excluded.modified_utc,
                    captured_utc = COALESCE(excluded.captured_utc, media_items.captured_utc),
                    last_seen_utc = excluded.last_seen_utc,
                    is_missing = 0,
                    width = COALESCE(excluded.width, media_items.width),
                    height = COALESCE(excluded.height, media_items.height)
                """;
            AddMediaParameters(command, item);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task UpdateMediaMetadataAsync(string path, int? width, int? height, DateTimeOffset? capturedUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE media_items
            SET width = COALESCE($width, width),
                height = COALESCE($height, height),
                captured_utc = COALESCE($captured, captured_utc)
            WHERE path = $path
            """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$width", (object?)width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)height ?? DBNull.Value);
        command.Parameters.AddWithValue("$captured", WriteNullableDate(capturedUtc));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MediaItem>> GetMediaItemsAsync(IEnumerable<string>? folderIds = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var ids = folderIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];
        var where = "f.is_enabled = 1";
        if (ids.Count > 0)
        {
            var parameters = ids.Select((_, index) => $"$folder{index}").ToList();
            where += $" AND m.folder_id IN ({string.Join(",", parameters)})";
            for (var index = 0; index < ids.Count; index++)
            {
                command.Parameters.AddWithValue($"$folder{index}", ids[index]);
            }
        }

        command.CommandText = $$"""
            SELECT
                m.id, m.folder_id, m.path, m.file_name, m.extension, m.folder_name, m.kind, m.size_bytes,
                m.created_utc, m.modified_utc, m.captured_utc, m.last_seen_utc, m.last_viewed_utc,
                m.view_count, m.is_missing, COALESCE(t.is_favorite, 0), COALESCE(t.is_hidden, 0),
                COALESCE(t.is_deletion_candidate, 0), COALESCE(t.is_watch_later, 0),
                COALESCE(t.is_folder_excluded, 0), f.is_private, m.width, m.height
            FROM media_items m
            JOIN registered_folders f ON f.id = m.folder_id
            LEFT JOIN media_tags t ON t.path = m.path
            WHERE {{where}}
            ORDER BY m.modified_utc DESC, m.path COLLATE NOCASE
            """;
        using var reader = command.ExecuteReader();
        var items = new List<MediaItem>();
        while (reader.Read())
        {
            items.Add(ReadMediaItem(reader));
        }

        return Task.FromResult<IReadOnlyList<MediaItem>>(items);
    }

    public Task SetMediaFlagAsync(string path, string flagName, bool value)
    {
        var column = GetMediaFlagColumn(flagName);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $$"""
            INSERT INTO media_tags (path, {{column}}, updated_utc)
            VALUES ($path, $value, $updated)
            ON CONFLICT(path) DO UPDATE SET
                {{column}} = excluded.{{column}},
                updated_utc = excluded.updated_utc
            """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$value", value ? 1 : 0);
        command.Parameters.AddWithValue("$updated", WriteDate(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<int> ClearMediaFlagAsync(string flagName)
    {
        var column = GetMediaFlagColumn(flagName);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $$"""
            UPDATE media_tags
            SET {{column}} = 0,
                updated_utc = $updated
            WHERE {{column}} <> 0
            """;
        command.Parameters.AddWithValue("$updated", WriteDate(DateTimeOffset.UtcNow));
        var changed = command.ExecuteNonQuery();
        return Task.FromResult(changed);
    }

    public Task RecordPlaybackAsync(string path, string? folderId, bool completed)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE media_items
                SET last_viewed_utc = $now,
                    view_count = view_count + 1
                WHERE path = $path
                """;
            update.Parameters.AddWithValue("$now", WriteDate(DateTimeOffset.UtcNow));
            update.Parameters.AddWithValue("$path", path);
            update.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO playback_history (path, folder_id, viewed_utc, completed)
                VALUES ($path, $folderId, $viewed, $completed)
                """;
            insert.Parameters.AddWithValue("$path", path);
            insert.Parameters.AddWithValue("$folderId", (object?)folderId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$viewed", WriteDate(DateTimeOffset.UtcNow));
            insert.Parameters.AddWithValue("$completed", completed ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task SaveShuffleStateAsync(ShuffleState state)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO shuffle_states (queue_key, state_json, updated_utc)
            VALUES ($key, $json, $updated)
            ON CONFLICT(queue_key) DO UPDATE SET
                state_json = excluded.state_json,
                updated_utc = excluded.updated_utc
            """;
        command.Parameters.AddWithValue("$key", state.QueueKey);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(state, _jsonOptions));
        command.Parameters.AddWithValue("$updated", WriteDate(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<ShuffleState?> LoadShuffleStateAsync(string queueKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state_json FROM shuffle_states WHERE queue_key = $key";
        command.Parameters.AddWithValue("$key", queueKey);
        var json = command.ExecuteScalar() as string;
        return Task.FromResult(json is null ? null : JsonSerializer.Deserialize<ShuffleState>(json, _jsonOptions));
    }

    public Task SaveSettingAsync<T>(string key, T value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value_json, updated_utc)
            VALUES ($key, $json, $updated)
            ON CONFLICT(key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_utc = excluded.updated_utc
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, _jsonOptions));
        command.Parameters.AddWithValue("$updated", WriteDate(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<T> LoadSettingAsync<T>(string key, T defaultValue)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(defaultValue);
        }

        return Task.FromResult(JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? defaultValue);
    }

    public Task<IReadOnlyList<FolderPlaylist>> GetPlaylistsAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, p.created_utc, p.updated_utc, pf.folder_id
            FROM playlists p
            LEFT JOIN playlist_folders pf ON pf.playlist_id = p.id
            ORDER BY p.updated_utc DESC, p.name COLLATE NOCASE, pf.sort_order
            """;
        using var reader = command.ExecuteReader();
        var playlists = new Dictionary<string, FolderPlaylist>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var id = reader.GetString(0);
            if (!playlists.TryGetValue(id, out var playlist))
            {
                playlist = new FolderPlaylist
                {
                    Id = id,
                    Name = reader.GetString(1),
                    CreatedUtc = ReadDate(reader, 2) ?? DateTimeOffset.UtcNow,
                    UpdatedUtc = ReadDate(reader, 3) ?? DateTimeOffset.UtcNow
                };
                playlists.Add(id, playlist);
            }

            if (!reader.IsDBNull(4))
            {
                playlist.FolderIds.Add(reader.GetString(4));
            }
        }

        return Task.FromResult<IReadOnlyList<FolderPlaylist>>(playlists.Values
            .Where(playlist => playlist.FolderIds.Count > 0)
            .ToList());
    }

    public Task UpsertPlaylistAsync(FolderPlaylist playlist)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO playlists (id, name, created_utc, updated_utc)
                VALUES ($id, $name, $created, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    updated_utc = excluded.updated_utc
                """;
            command.Parameters.AddWithValue("$id", playlist.Id);
            command.Parameters.AddWithValue("$name", playlist.Name);
            command.Parameters.AddWithValue("$created", WriteDate(playlist.CreatedUtc));
            command.Parameters.AddWithValue("$updated", WriteDate(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM playlist_folders WHERE playlist_id = $id";
            delete.Parameters.AddWithValue("$id", playlist.Id);
            delete.ExecuteNonQuery();
        }

        var order = 0;
        foreach (var folderId in playlist.FolderIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO playlist_folders (playlist_id, folder_id, sort_order)
                SELECT $playlistId, $folderId, $sortOrder
                WHERE EXISTS (SELECT 1 FROM registered_folders WHERE id = $folderId)
                """;
            insert.Parameters.AddWithValue("$playlistId", playlist.Id);
            insert.Parameters.AddWithValue("$folderId", folderId);
            insert.Parameters.AddWithValue("$sortOrder", order++);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task DeletePlaylistAsync(string playlistId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM playlists WHERE id = $id";
        command.Parameters.AddWithValue("$id", playlistId);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<int> CountMediaItemsAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM media_items";
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    private static void RemoveEmptyPlaylists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM playlists
            WHERE id NOT IN (SELECT DISTINCT playlist_id FROM playlist_folders)
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void AddMediaParameters(SqliteCommand command, MediaItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$folderId", item.FolderId);
        command.Parameters.AddWithValue("$path", item.Path);
        command.Parameters.AddWithValue("$fileName", item.FileName);
        command.Parameters.AddWithValue("$extension", item.Extension);
        command.Parameters.AddWithValue("$folderName", item.FolderName);
        command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$size", item.SizeBytes);
        command.Parameters.AddWithValue("$created", WriteDate(item.CreatedUtc));
        command.Parameters.AddWithValue("$modified", WriteDate(item.ModifiedUtc));
        command.Parameters.AddWithValue("$captured", WriteNullableDate(item.CapturedUtc));
        command.Parameters.AddWithValue("$seen", WriteDate(item.LastSeenUtc));
        command.Parameters.AddWithValue("$viewed", WriteNullableDate(item.LastViewedUtc));
        command.Parameters.AddWithValue("$viewCount", item.ViewCount);
        command.Parameters.AddWithValue("$missing", item.IsMissing ? 1 : 0);
        command.Parameters.AddWithValue("$width", (object?)item.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)item.Height ?? DBNull.Value);
    }

    private static MediaItem ReadMediaItem(SqliteDataReader reader)
    {
        return new MediaItem
        {
            Id = reader.GetString(0),
            FolderId = reader.GetString(1),
            Path = reader.GetString(2),
            FileName = reader.GetString(3),
            Extension = reader.GetString(4),
            FolderName = reader.GetString(5),
            Kind = (MediaKind)reader.GetInt32(6),
            SizeBytes = reader.GetInt64(7),
            CreatedUtc = ReadDate(reader, 8) ?? DateTimeOffset.UtcNow,
            ModifiedUtc = ReadDate(reader, 9) ?? DateTimeOffset.UtcNow,
            CapturedUtc = ReadDate(reader, 10),
            LastSeenUtc = ReadDate(reader, 11) ?? DateTimeOffset.UtcNow,
            LastViewedUtc = ReadDate(reader, 12),
            ViewCount = reader.GetInt32(13),
            IsMissing = reader.GetInt32(14) != 0,
            IsFavorite = reader.GetInt32(15) != 0,
            IsHidden = reader.GetInt32(16) != 0,
            IsDeletionCandidate = reader.GetInt32(17) != 0,
            IsWatchLater = reader.GetInt32(18) != 0,
            IsFolderExcluded = reader.GetInt32(19) != 0,
            IsFromPrivateFolder = reader.GetInt32(20) != 0,
            Width = reader.IsDBNull(21) ? null : reader.GetInt32(21),
            Height = reader.IsDBNull(22) ? null : reader.GetInt32(22)
        };
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return DateTimeOffset.TryParse(reader.GetString(ordinal), out var value) ? value : null;
    }

    private static string WriteDate(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private static object WriteNullableDate(DateTimeOffset? value)
        => value.HasValue ? WriteDate(value.Value) : DBNull.Value;

    private static string GetMediaFlagColumn(string flagName)
        => flagName switch
        {
            nameof(MediaItem.IsFavorite) => "is_favorite",
            nameof(MediaItem.IsHidden) => "is_hidden",
            nameof(MediaItem.IsDeletionCandidate) => "is_deletion_candidate",
            nameof(MediaItem.IsWatchLater) => "is_watch_later",
            nameof(MediaItem.IsFolderExcluded) => "is_folder_excluded",
            _ => throw new ArgumentOutOfRangeException(nameof(flagName), flagName, "Unknown media flag")
        };
}
