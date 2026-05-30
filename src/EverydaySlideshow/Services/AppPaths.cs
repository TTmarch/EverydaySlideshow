using System.IO;
using EverydaySlideshow.Core;

namespace EverydaySlideshow.Services;

public static class AppPaths
{
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppName);

    public static string DatabasePath => Path.Combine(AppDataRoot, AppConstants.DatabaseFileName);

    public static string CacheRoot => Path.Combine(AppDataRoot, "Cache");

    public static string ThumbnailRoot => Path.Combine(CacheRoot, "Thumbnails");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(ThumbnailRoot);
    }
}
