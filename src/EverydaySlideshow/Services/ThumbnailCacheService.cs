using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace EverydaySlideshow.Services;

public sealed class ThumbnailCacheService
{
    public async Task WarmAsync(IEnumerable<string> imagePaths, CancellationToken cancellationToken = default)
    {
        foreach (var path in imagePaths.Take(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureThumbnailAsync(path, cancellationToken);
        }
    }

    public async Task<string?> EnsureThumbnailAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            return null;
        }

        var target = GetThumbnailPath(imagePath);
        if (File.Exists(target))
        {
            return target;
        }

        try
        {
            var result = await BitmapMediaLoader.LoadImageAsync(imagePath, decodePixelWidth: 360, cancellationToken);
            if (result.Source is not BitmapSource source)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = File.Create(target);
            var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(output);
            return target;
        }
        catch
        {
            return null;
        }
    }

    private static string GetThumbnailPath(string imagePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(imagePath.ToUpperInvariant()));
        return Path.Combine(AppPaths.ThumbnailRoot, $"{Convert.ToHexString(hash).ToLowerInvariant()}.jpg");
    }
}
