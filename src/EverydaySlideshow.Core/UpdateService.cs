using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverydaySlideshow.Core;

public enum UpdateCheckStatus
{
    UpdateAvailable = 0,
    UpToDate = 1,
    NotConfigured = 2,
    NoReleaseFound = 3,
    NetworkError = 4,
    InvalidResponse = 5
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? LatestVersion = null,
    string? LatestTag = null,
    string? ReleaseUrl = null,
    string? Message = null)
{
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable;
}

public sealed class GitHubUpdateService
{
    private readonly HttpClient _httpClient;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(
        string repositoryOwner,
        string repositoryName,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryOwner)
            || string.IsNullOrWhiteSpace(repositoryName)
            || IsPlaceholder(repositoryOwner)
            || IsPlaceholder(repositoryName))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotConfigured);
        }

        var requestUri = new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(repositoryOwner)}/{Uri.EscapeDataString(repositoryName)}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd($"{AppConstants.AppName}/{currentVersion}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, Message: ex.Message);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleaseFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, Message: $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        GitHubReleaseResponse? release;
        try
        {
            release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(
                responseStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
        }
        catch (JsonException ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse, Message: ex.Message);
        }

        if (release is null || string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
        }

        if (!TryParseVersion(release.TagName, out var latestVersion))
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse, LatestTag: release.TagName);
        }

        return Normalize(latestVersion) > Normalize(currentVersion)
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latestVersion, release.TagName, release.HtmlUrl)
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate, latestVersion, release.TagName, release.HtmlUrl);
    }

    private static bool IsPlaceholder(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Equals("REPOSITORY_OWNER", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("REPOSITORY_NAME", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("<repo-owner>", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("<repo-name>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }

    private static Version Normalize(Version version)
        => new(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
