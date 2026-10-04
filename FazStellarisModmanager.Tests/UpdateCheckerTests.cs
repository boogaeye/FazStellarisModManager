using System.Net;
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
}
