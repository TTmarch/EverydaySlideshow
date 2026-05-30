using System.Net;
using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckLatestAsync_reports_update_available()
    {
        var service = CreateService(HttpStatusCode.OK, """{"tag_name":"v1.2.0","html_url":"https://github.com/owner/repo/releases/tag/v1.2.0"}""");

        var result = await service.CheckLatestAsync("owner", "repo", new Version(1, 1, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
        Assert.Equal("https://github.com/owner/repo/releases/tag/v1.2.0", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckLatestAsync_reports_up_to_date()
    {
        var service = CreateService(HttpStatusCode.OK, """{"tag_name":"v1.1.0","html_url":"https://github.com/owner/repo/releases/tag/v1.1.0"}""");

        var result = await service.CheckLatestAsync("owner", "repo", new Version(1, 1, 0));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task CheckLatestAsync_reports_not_configured_for_placeholders()
    {
        var service = CreateService(HttpStatusCode.OK, "{}");

        var result = await service.CheckLatestAsync("REPOSITORY_OWNER", "repo", new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task CheckLatestAsync_reports_no_release_found_for_404()
    {
        var service = CreateService(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        var result = await service.CheckLatestAsync("owner", "repo", new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.NoReleaseFound, result.Status);
    }

    [Fact]
    public async Task CheckLatestAsync_reports_invalid_response_for_bad_json()
    {
        var service = CreateService(HttpStatusCode.OK, "{bad json");

        var result = await service.CheckLatestAsync("owner", "repo", new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task CheckLatestAsync_reports_network_error()
    {
        var service = new GitHubUpdateService(new HttpClient(new ThrowingHandler()));

        var result = await service.CheckLatestAsync("owner", "repo", new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
    }

    private static GitHubUpdateService CreateService(HttpStatusCode statusCode, string content)
        => new(new HttpClient(new ResponseHandler(statusCode, content)));

    private sealed class ResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}
