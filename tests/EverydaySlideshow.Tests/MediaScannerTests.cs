using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

public sealed class MediaScannerTests
{
    [Fact]
    public async Task ScanFolderAsync_reads_supported_files_and_subfolders_without_decoding()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("root.jpg");
        temp.CreateFile("sub\\child.png");
        temp.CreateFile("sub\\movie.mp4");
        temp.CreateFile("ignore.txt");

        var folder = new FolderProfile
        {
            Id = "scan",
            Name = "scan",
            Path = temp.Root,
            IncludeSubfolders = true
        };

        var items = await new MediaScanner().ScanFolderAsync(folder);

        Assert.Equal(3, items.Count);
        Assert.Contains(items, item => item.FileName == "root.jpg" && item.Kind == MediaKind.Image);
        Assert.Contains(items, item => item.FileName == "movie.mp4" && item.Kind == MediaKind.Video);
    }

    [Fact]
    public async Task ScanFolderAsync_respects_subfolder_setting_and_missing_root()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("root.jpg");
        temp.CreateFile("sub\\child.png");

        var scanner = new MediaScanner();
        var shallow = await scanner.ScanFolderAsync(new FolderProfile
        {
            Id = "shallow",
            Path = temp.Root,
            IncludeSubfolders = false
        });
        var missing = await scanner.ScanFolderAsync(new FolderProfile
        {
            Id = "missing",
            Path = System.IO.Path.Combine(temp.Root, "missing"),
            IncludeSubfolders = true
        });

        Assert.Single(shallow);
        Assert.Empty(missing);
    }

    [Fact]
    public async Task ScanFolderAsync_handles_large_file_lists()
    {
        using var temp = new TempDirectory();
        for (var index = 0; index < 600; index++)
        {
            temp.CreateFile($"bulk\\IMG_20250101_{index:D4}.jpg");
        }

        var items = await new MediaScanner().ScanFolderAsync(new FolderProfile
        {
            Id = "bulk",
            Path = temp.Root,
            IncludeSubfolders = true
        });

        Assert.Equal(600, items.Count);
        Assert.All(items, item => Assert.Equal(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero), item.CapturedUtc));
    }
}
