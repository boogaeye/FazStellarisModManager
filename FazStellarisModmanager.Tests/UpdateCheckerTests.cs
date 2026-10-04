using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FazStellarisModmanager.Core.Updates;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class UpdateCheckerTests
{
    public static string Release(string tag, params (string Name, string Url, long Size)[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            name = "Release " + tag,
            body = "Notes",
            html_url = "https://github.com/owner/repo/releases/tag/" + tag,
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url, size = a.Size }),
        });

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.0-beta+abc", "2.0.0")]
    [InlineData("v3", "3.0.0")]
    [InlineData(" v0.1.0 ", "0.1.0")]
    [InlineData("0.1.0+9f1c2d", "0.1.0")]
    public void Reads_version_tags(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(Version.Parse(expected), v);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1.x")]
    public void Rejects_tags_that_are_not_versions(string tag) => Assert.False(UpdateChecker.TryParseVersion(tag, out _));

    [Fact]
    public void Parses_a_release_and_picks_the_windows_zip()
    {
        var info = UpdateChecker.Parse(Release("v0.2.0",
            ("Source.zip", "https://x/source.zip", 5),
            ("FazStellarisModManager-WIN-X64.zip", "https://x/app.zip", 123)))!;

        Assert.Equal(new Version(0, 2, 0), info.Version);
        Assert.Equal(("v0.2.0", "Release v0.2.0", "Notes"), (info.Tag, info.Name, info.Notes));
        Assert.Equal("https://github.com/owner/repo/releases/tag/v0.2.0", info.PageUrl);
        Assert.Equal(("https://x/app.zip", 123L), (info.DownloadUrl, info.Size));
    }

    [Fact]
    public void A_release_without_the_zip_has_no_download_and_bad_documents_give_null()
    {
        var info = UpdateChecker.Parse(Release("v0.2.0"))!;
        Assert.Null(info.DownloadUrl);
        Assert.Equal(0, info.Size);
        Assert.Null(UpdateChecker.Parse(Release("nightly")));
        Assert.Null(UpdateChecker.Parse("[]"));
    }

    static UpdateChecker Checker(FakeHttp http) => new(new HttpClient(http), "owner/repo");

    [Fact]
    public async Task A_newer_release_is_available_and_the_request_identifies_the_app()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Release("v0.2.0")));

        var result = await Checker(http).CheckAsync(new Version(0, 1, 0, 0));

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal(new Version(0, 2, 0), result.Update!.Version);
        Assert.Equal(new Version(0, 1, 0), result.Current);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", request.RequestUri!.ToString());
        Assert.Contains("FazStellarisModManager", request.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    public async Task The_same_or_an_older_release_is_up_to_date(string tag)
    {
        var result = await Checker(new FakeHttp(_ => FakeHttp.Json(Release(tag)))).CheckAsync(new Version(0, 1, 0));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Not_found_means_no_releases_and_other_problems_fail_quietly()
    {
        var current = new Version(0, 1, 0);
        Assert.Equal(UpdateStatus.NoReleases,
            (await Checker(new FakeHttp(_ => FakeHttp.Json("{}", HttpStatusCode.NotFound))).CheckAsync(current)).Status);

        var server = await Checker(new FakeHttp(_ => FakeHttp.Json("{}", HttpStatusCode.InternalServerError))).CheckAsync(current);
        Assert.Equal(UpdateStatus.Failed, server.Status);
        Assert.Contains("500", server.Error);

        var offline = await Checker(new FakeHttp(_ => throw new HttpRequestException("offline"))).CheckAsync(current);
        Assert.Equal((UpdateStatus.Failed, "offline"), (offline.Status, offline.Error));

        Assert.Equal(UpdateStatus.Failed, (await Checker(new FakeHttp(_ => FakeHttp.Json("not json"))).CheckAsync(current)).Status);
        Assert.Equal(UpdateStatus.Failed, (await Checker(new FakeHttp(_ => FakeHttp.Json(Release("nightly")))).CheckAsync(current)).Status);
    }

    [Fact]
    public void Reads_the_sha256_digest_and_ignores_a_size_that_is_not_a_number()
    {
        const string hex = "0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef";
        var json = $$"""
            { "tag_name": "v0.2.0", "assets": [
                { "name": "App-win-x64.zip", "browser_download_url": "https://x/app.zip", "size": "big", "digest": "sha256:{{hex}}" } ] }
            """;
        var info = UpdateChecker.Parse(json)!;
        Assert.Equal(0, info.Size);
        Assert.Equal(hex.ToLowerInvariant(), info.Sha256);

        Assert.Null(UpdateChecker.Parse(json.Replace("sha256:", "md5:"))!.Sha256);
        Assert.Null(UpdateChecker.Parse(json.Replace("sha256:0123", "sha256:xyz3"))!.Sha256);
        Assert.Null(UpdateChecker.Parse(Release("v0.2.0", ("App-win-x64.zip", "https://x/app.zip", 5)))!.Sha256);
    }

    [Fact]
    public async Task A_bogus_charset_or_a_non_number_size_does_not_throw()
    {
        var current = new Version(0, 1, 0);
        var bogus = await Checker(new FakeHttp(_ =>
        {
            var content = new ByteArrayContent("{}"u8.ToArray());
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=bogus-charset");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        })).CheckAsync(current);
        Assert.Equal(UpdateStatus.Failed, bogus.Status);
        Assert.False(string.IsNullOrEmpty(bogus.Error));

        var json = """{ "tag_name": "v0.2.0", "assets": [ { "name": "App-win-x64.zip", "browser_download_url": "https://x/app.zip", "size": "big" } ] }""";
        var odd = await Checker(new FakeHttp(_ => FakeHttp.Json(json))).CheckAsync(current);
        Assert.Equal(UpdateStatus.Available, odd.Status);
        Assert.Equal(0, odd.Update!.Size);
    }

    [Fact]
    public async Task Any_other_exception_fails_quietly_but_caller_cancellation_throws()
    {
        var current = new Version(0, 1, 0);
        var odd = await Checker(new FakeHttp(_ => throw new InvalidOperationException("odd"))).CheckAsync(current);
        Assert.Equal((UpdateStatus.Failed, "odd"), (odd.Status, odd.Error));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Checker(new FakeHttp(_ => throw new OperationCanceledException(cts.Token))).CheckAsync(current, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task An_exhausted_rate_limit_says_so(HttpStatusCode status)
    {
        var limited = await Checker(new FakeHttp(_ =>
        {
            var r = FakeHttp.Json("{}", status);
            r.Headers.Add("x-ratelimit-remaining", "0");
            return r;
        })).CheckAsync(new Version(0, 1, 0));
        Assert.Equal((UpdateStatus.Failed, "GitHub's rate limit was reached; try again later."), (limited.Status, limited.Error));

        var forbidden = await Checker(new FakeHttp(_ => FakeHttp.Json("{}", HttpStatusCode.Forbidden))).CheckAsync(new Version(0, 1, 0));
        Assert.Contains("403", forbidden.Error);
    }
}
