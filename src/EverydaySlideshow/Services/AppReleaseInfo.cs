using System.Reflection;

namespace EverydaySlideshow.Services;

internal static class AppReleaseInfo
{
    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 3);

    public static string RepositoryOwner { get; } = ReadMetadata("RepositoryOwner");

    public static string RepositoryName { get; } = ReadMetadata("RepositoryName");

    private static string ReadMetadata(string key)
        => Assembly.GetExecutingAssembly()
               .GetCustomAttributes<AssemblyMetadataAttribute>()
               .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
               ?.Value
           ?? "";
}
