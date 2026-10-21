using System.Net;
using System.Text.Json;
using Virgil.Core.Releases;
using Virgil.Infrastructure.Releases;
using Xunit;

namespace Virgil.Tests;
public sealed class VirgilReleaseSourceTests
{
    private const string Page = "https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha";
    private static string Release(string tag = "v2.0.0-alpha", bool draft = false, string url = Page) => JsonSerializer.Serialize(new {
        tag_name = tag, draft, prerelease = true, published_at = "2026-10-20T12:00:00Z", html_url = url,
        assets = new[] { new { browser_download_url = "https://example.com/never-download.exe" } }
    });
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };

    [Fact]
    public async Task IncludesPrereleasesSkipsDraftAndInvalidTagAndOnlyRequestsAnonymousMetadata()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, ct) => {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(GitHubVirgilReleaseSource.ReleasesEndpoint + "?per_page=100&page=1", request.RequestUri!.AbsoluteUri);
            Assert.Contains("Virgil", request.Headers.UserAgent.ToString());
            Assert.Equal("application/vnd.github+json", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Content);
            Assert.Equal(2, request.Headers.Count());
            Assert.True(ct.CanBeCanceled);
            return Task.FromResult(Response("[" + Release() + "," + Release("v9.0.0", true) + "," + Release("evil") + "]"));
        }));
        var result = Assert.IsType<ReleaseSourceResult.Published>(await new GitHubVirgilReleaseSource(client).ReadAsync(default));
        var release = Assert.Single(result.Releases);
        Assert.Equal("2.0.0-alpha", release.Version.Value);
        Assert.Equal(Page, release.Page.AbsoluteUri);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData(403, ReleaseFailure.ApiLimit)][InlineData(429, ReleaseFailure.ApiLimit)]
    [InlineData(500, ReleaseFailure.OfflineOrTimeout)][InlineData(503, ReleaseFailure.OfflineOrTimeout)]
    [InlineData(404, ReleaseFailure.InvalidResponse)]
    [InlineData(401, ReleaseFailure.InvalidResponse)]
    [InlineData(408, ReleaseFailure.OfflineOrTimeout)]
    public async Task HttpFailuresAreExplicit(int status, ReleaseFailure expected)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response("", (HttpStatusCode)status))));
        Assert.Equal(expected, Assert.IsType<ReleaseSourceResult.Unavailable>(await new GitHubVirgilReleaseSource(client).ReadAsync(default)).Reason);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task OfflineAndTimeoutAreHandled(bool timeout)
    {
        using var client = new HttpClient(new Handler((_, _) => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
            : Task.FromException<HttpResponseMessage>(new HttpRequestException())));
        Assert.Equal(ReleaseFailure.OfflineOrTimeout, Assert.IsType<ReleaseSourceResult.Unavailable>(await new GitHubVirgilReleaseSource(client).ReadAsync(default)).Reason);
    }
    [Fact]
    public async Task CallerCancellationIsPropagatedAndReported()
    {
        using var stop = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, ct) => { stop.Cancel(); ct.ThrowIfCancellationRequested(); throw new InvalidOperationException(); }));
        Assert.Equal(ReleaseFailure.Cancelled, Assert.IsType<ReleaseSourceResult.Unavailable>(await new GitHubVirgilReleaseSource(client).ReadAsync(stop.Token)).Reason);
    }
    [Theory]
    [InlineData("[]", ReleaseFailure.NoPublishedRelease)]
    [InlineData("not json", ReleaseFailure.InvalidResponse)]
    [InlineData("{}", ReleaseFailure.InvalidResponse)]
    [InlineData("[{}]", ReleaseFailure.InvalidResponse)]
    [InlineData("[{\"draft\":false,\"tag_name\":\"v2.0.0\",\"published_at\":null}]", ReleaseFailure.InvalidResponse)]
    public async Task EmptyAndInvalidJsonHaveDistinctStates(string json, ReleaseFailure expected)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(json))));
        Assert.Equal(expected, Assert.IsType<ReleaseSourceResult.Unavailable>(await new GitHubVirgilReleaseSource(client).ReadAsync(default)).Reason);
    }
    [Theory]
    [InlineData("https://evil.example/release")][InlineData("file:///tmp/setup.exe")]
    [InlineData("https://github.com/bassetthomas-design/virgil-2.0/releases/download/setup.exe")]
    public void ReleaseLinksCannotTargetDownloadsOrOtherHosts(string url) =>
        Assert.Equal(ReleaseFailure.InvalidResponse, Assert.IsType<ReleaseSourceResult.Unavailable>(GitHubVirgilReleaseSource.ParsePage("[" + Release(url: url) + "]", out _)).Reason);
    [Fact]
    public async Task PaginationFindsReleasesBeyondFirstPageWithoutFollowingRemoteLinks()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) => {
            calls++;
            Assert.EndsWith("page=" + calls, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response(calls == 1 ? "[" + string.Join(",", Enumerable.Repeat(Release("invalid"), 100)) + "]" : "[" + Release() + "]"));
        }));
        Assert.Single(Assert.IsType<ReleaseSourceResult.Published>(await new GitHubVirgilReleaseSource(client).ReadAsync(default)).Releases);
        Assert.Equal(2, calls);
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task OnlyDraftsOrInvalidTagsMeansNoPublishedRelease(bool draft)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response("[" + Release(draft ? "v2.0.0" : "invalid", draft) + "]"))));
        Assert.Equal(ReleaseFailure.NoPublishedRelease, Assert.IsType<ReleaseSourceResult.Unavailable>(await new GitHubVirgilReleaseSource(client).ReadAsync(default)).Reason);
    }
}
