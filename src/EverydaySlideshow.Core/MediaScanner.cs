using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EverydaySlideshow.Core;

public sealed class MediaScanner
{
    private static readonly Regex DateInName = new(
        @"(?<year>19\d{2}|20\d{2})[-_\.]?(?<month>0[1-9]|1[0-2])[-_\.]?(?<day>0[1-9]|[12]\d|3[01])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<IReadOnlyList<MediaItem>> ScanFolderAsync(
        FolderProfile folder,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var found = new List<MediaItem>();
            if (!Directory.Exists(folder.Path))
            {
                return (IReadOnlyList<MediaItem>)found;
            }

            foreach (var filePath in EnumerateFilesSafe(folder.Path, folder.IncludeSubfolders, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = System.IO.Path.GetExtension(filePath);
                if (!AppConstants.IsSupportedMediaExtension(extension))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(filePath);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    found.Add(new MediaItem
                    {
                        Id = CreateStableId(filePath),
                        FolderId = folder.Id,
                        Path = info.FullName,
                        FileName = info.Name,
                        Extension = extension.ToLowerInvariant(),
                        FolderName = info.Directory?.Name ?? "",
                        Kind = AppConstants.GetMediaKind(extension),
                        SizeBytes = info.Length,
                        CreatedUtc = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                        ModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        CapturedUtc = TryParseDateFromFileName(info.Name),
                        LastSeenUtc = DateTimeOffset.UtcNow,
                        IsFromPrivateFolder = folder.IsPrivate
                    });

                    if (found.Count % 100 == 0)
                    {
                        progress?.Report(new ScanProgress(folder.Id, found.Count, info.FullName));
                    }
                }
                catch (IOException)
                {
                    // Slow or disconnected storage can throw while scanning; skip and continue.
                }
                catch (UnauthorizedAccessException)
                {
                    // A slideshow should never stop because a single file cannot be read.
                }
            }

            progress?.Report(new ScanProgress(folder.Id, found.Count, null));
            return (IReadOnlyList<MediaItem>)found;
        }, cancellationToken);
    }

    public static string CreateStableId(string path)
    {
        var normalized = System.IO.Path.GetFullPath(path).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, bool includeSubfolders, CancellationToken cancellationToken)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }

        if (!includeSubfolders)
        {
            yield break;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in EnumerateFilesSafe(directory, includeSubfolders: true, cancellationToken))
            {
                yield return file;
            }
        }
    }

    private static DateTimeOffset? TryParseDateFromFileName(string fileName)
    {
        var match = DateInName.Match(fileName);
        if (!match.Success)
        {
            return null;
        }

        if (int.TryParse(match.Groups["year"].Value, out var year)
            && int.TryParse(match.Groups["month"].Value, out var month)
            && int.TryParse(match.Groups["day"].Value, out var day)
            && DateTimeOffset.TryParse($"{year:D4}-{month:D2}-{day:D2}T12:00:00Z", out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
