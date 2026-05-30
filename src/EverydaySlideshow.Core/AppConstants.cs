namespace EverydaySlideshow.Core;

public static class AppConstants
{
    public const string AppName = "EverydaySlideshow";
    public const string DisplayName = "EverydaySlideshow";
    public const string DatabaseFileName = "slideshow.db";

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp",
        ".heic", ".heif", ".avif",
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw"
    };

    public static readonly HashSet<string> RequiredImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    public static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw"
    };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".webm", ".wmv", ".avi", ".mkv"
    };

    public static bool IsSupportedMediaExtension(string extension)
        => ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension);

    public static MediaKind GetMediaKind(string extension)
        => VideoExtensions.Contains(extension) ? MediaKind.Video : MediaKind.Image;
}
