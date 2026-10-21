using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Virgil.Core.Releases;

namespace Virgil.Infrastructure.Releases;

/// <summary>Lecture anonyme de métadonnées publiques uniquement. Aucun accès aux assets.</summary>
public sealed class GitHubVirgilReleaseSource(HttpClient client) : IVirgilReleaseSource
{
    public const string ReleasesEndpoint = "https://api.github.com/repos/bassetthomas-design/virgil-2.0/releases";
    public static bool IsReleasePage(Uri page) => page.IsAbsoluteUri && page.Scheme == Uri.UriSchemeHttps
        && page.Host == "github.com" && page.IsDefaultPort && page.UserInfo.Length == 0
        && page.Query.Length == 0 && page.Fragment.Length == 0
        && page.AbsolutePath.StartsWith("/bassetthomas-design/virgil-2.0/releases/tag/", StringComparison.Ordinal)
        && page.AbsolutePath.Length > "/bassetthomas-design/virgil-2.0/releases/tag/".Length;

    public async Task<ReleaseSourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var releases = new List<VirgilRelease>();
            for (var page = 1; ; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint + $"?per_page=100&page={page}");
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Virgil", null));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                using var response = await client.SendAsync(request, deadline.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    return new ReleaseSourceResult.Unavailable(ReleaseFailure.ApiLimit);
                if (!response.IsSuccessStatusCode) return new ReleaseSourceResult.Unavailable(
                    response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500
                        ? ReleaseFailure.OfflineOrTimeout : ReleaseFailure.InvalidResponse);
                var json = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
                var parsed = ParsePage(json, out var count);
                if (parsed is ReleaseSourceResult.Unavailable) return parsed;
                releases.AddRange(((ReleaseSourceResult.Published)parsed).Releases);
                if (count < 100) return releases.Count == 0
                    ? new ReleaseSourceResult.Unavailable(ReleaseFailure.NoPublishedRelease)
                    : new ReleaseSourceResult.Published(releases);
            }
        }
        catch (OperationCanceledException)
        {
            return new ReleaseSourceResult.Unavailable(cancellationToken.IsCancellationRequested
                ? ReleaseFailure.Cancelled : ReleaseFailure.OfflineOrTimeout);
        }
        catch (HttpRequestException) { return new ReleaseSourceResult.Unavailable(ReleaseFailure.OfflineOrTimeout); }
        catch (Exception) { return new ReleaseSourceResult.Unavailable(ReleaseFailure.InvalidResponse); }
    }

    public static ReleaseSourceResult ParsePage(string json, out int count)
    {
        count = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
            count = document.RootElement.GetArrayLength();
            var releases = new List<VirgilRelease>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.GetProperty("draft").GetBoolean()) continue;
                var version = SemanticVersion.ParseTag(item.GetProperty("tag_name").GetString());
                if (version is null) continue;
                if (!item.GetProperty("published_at").TryGetDateTimeOffset(out var date)
                    || !Uri.TryCreate(item.GetProperty("html_url").GetString(), UriKind.Absolute, out var page)
                    || !IsReleasePage(page)) throw new JsonException();
                releases.Add(new VirgilRelease(version, date, page));
            }
            return new ReleaseSourceResult.Published(releases);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new ReleaseSourceResult.Unavailable(ReleaseFailure.InvalidResponse); }
    }
}
