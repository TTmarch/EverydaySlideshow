using System.IO;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EverydaySlideshow.Services;

public sealed record ImageLoadResult(ImageSource? Source, int? Width, int? Height, DateTimeOffset? CapturedUtc);

public static class BitmapMediaLoader
{
    public static Task<ImageLoadResult> LoadImageAsync(string path, int decodePixelWidth = 3840, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return new ImageLoadResult(null, null, null, null);
            }

            try
            {
                ushort orientation = 1;
                DateTimeOffset? captured = null;
                using (var stream = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                        BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames.FirstOrDefault();
                    orientation = ReadOrientation(frame);
                    captured = ReadCapturedDate(frame);
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                }
                bitmap.EndInit();
                bitmap.Freeze();

                var source = ApplyOrientation(bitmap, orientation);
                if (source.CanFreeze)
                {
                    source.Freeze();
                }

                return new ImageLoadResult(source, source.PixelWidth, source.PixelHeight, captured);
            }
            catch
            {
                return new ImageLoadResult(null, null, null, null);
            }
        }, cancellationToken);
    }

    private static ushort ReadOrientation(BitmapFrame? frame)
    {
        if (frame?.Metadata is not BitmapMetadata metadata)
        {
            return 1;
        }

        try
        {
            var value = metadata.GetQuery("/app1/ifd/{ushort=274}");
            return value switch
            {
                ushort orientation => orientation,
                short orientation => (ushort)orientation,
                _ => 1
            };
        }
        catch
        {
            return 1;
        }
    }

    private static DateTimeOffset? ReadCapturedDate(BitmapFrame? frame)
    {
        if (frame?.Metadata is not BitmapMetadata metadata)
        {
            return null;
        }

        foreach (var query in new[] { "/app1/ifd/exif/{ushort=36867}", "/app1/ifd/{ushort=306}" })
        {
            try
            {
                if (metadata.GetQuery(query) is string text)
                {
                    foreach (var format in new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" })
                    {
                        if (DateTimeOffset.TryParseExact(
                                text,
                                format,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeLocal,
                                out var parsed))
                        {
                            return parsed.ToUniversalTime();
                        }
                    }
                }
            }
            catch
            {
                // Some WIC codecs do not expose EXIF queries consistently.
            }
        }

        return null;
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, ushort orientation)
    {
        Transform? transform = orientation switch
        {
            3 => new RotateTransform(180),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            _ => null
        };

        return transform is null ? source : new TransformedBitmap(source, transform);
    }
}
