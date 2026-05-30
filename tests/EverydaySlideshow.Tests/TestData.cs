using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"EverydaySlideshow.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateFile(string relativePath)
    {
        var path = System.IO.Path.Combine(Root, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Temp cleanup should not hide the test result.
        }
    }
}

internal static class TestData
{
    public static MediaItem Media(
        string path,
        string folderId = "folder",
        string? folderName = null,
        bool favorite = false,
        bool hidden = false,
        bool deletionCandidate = false,
        bool watchLater = false,
        bool folderExcluded = false,
        bool privateFolder = false,
        MediaKind kind = MediaKind.Image,
        DateTimeOffset? modifiedUtc = null,
        DateTimeOffset? capturedUtc = null,
        DateTimeOffset? lastViewedUtc = null,
        int? width = 1600,
        int? height = 1000)
    {
        var fileName = System.IO.Path.GetFileName(path);
        return new MediaItem
        {
            Id = MediaScanner.CreateStableId(path),
            FolderId = folderId,
            Path = path,
            FileName = fileName,
            Extension = System.IO.Path.GetExtension(path).ToLowerInvariant(),
            FolderName = folderName ?? new DirectoryInfo(System.IO.Path.GetDirectoryName(path) ?? "").Name,
            Kind = kind,
            SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
            CreatedUtc = modifiedUtc ?? DateTimeOffset.UtcNow.AddDays(-10),
            ModifiedUtc = modifiedUtc ?? DateTimeOffset.UtcNow.AddDays(-10),
            CapturedUtc = capturedUtc,
            LastSeenUtc = DateTimeOffset.UtcNow,
            LastViewedUtc = lastViewedUtc,
            IsFavorite = favorite,
            IsHidden = hidden,
            IsDeletionCandidate = deletionCandidate,
            IsWatchLater = watchLater,
            IsFolderExcluded = folderExcluded,
            IsFromPrivateFolder = privateFolder,
            Width = width,
            Height = height
        };
    }
}
