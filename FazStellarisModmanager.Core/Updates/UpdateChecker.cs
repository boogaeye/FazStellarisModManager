using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Updates;

/// <summary>
/// A published release. <see cref="DownloadUrl"/> is null when the release has no Windows zip.
/// <see cref="Sha256"/> is the zip's lowercase hex SHA-256 from GitHub's asset digest, or null when GitHub gave none.
/// </summary>
public sealed record UpdateInfo(Version Version, string Tag, string Name, string? Notes, string PageUrl, string? DownloadUrl, long Size, string? Sha256 = null);

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateCheckResult(UpdateStatus Status, Version Current, UpdateInfo? Update = null, string? Error = null);

/// <summary>Asks GitHub for the latest release (drafts and pre-releases excluded) and compares it with the running version.</summary>
public sealed class UpdateChecker(HttpClient http, string repository = UpdateChecker.DefaultRepository)
{
    public const string DefaultRepository = "boogaeye/FazStellarisModManager";

    /// <summary>The release asset the installer uses: a self-contained Windows x64 build.</summary>
    public const string AssetSuffix = "-win-x64.zip";

    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Never throws except when <paramref name="ct"/> is cancelled: every problem comes back as <see cref="UpdateStatus.Failed"/> with a message.</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        current = Normalize(current);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("FazStellarisModManager", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheckResult(UpdateStatus.NoReleases, current);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Contains("0"))
                return new UpdateCheckResult(UpdateStatus.Failed, current, Error: "GitHub's rate limit was reached; try again later.");
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateStatus.Failed, current, Error: $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            var release = Parse(await response.Content.ReadAsStringAsync(timeout.Token), repository);
            if (release is null)
                return new UpdateCheckResult(UpdateStatus.Failed, current, Error: "The latest release has no version tag like v1.2.3.");
            return release.Version > current
                ? new UpdateCheckResult(UpdateStatus.Available, current, release)
                : new UpdateCheckResult(UpdateStatus.UpToDate, current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, current,
                Error: ex is OperationCanceledException ? "GitHub did not answer in time." : ex.Message);
        }
    }

    /// <summary>The release in a GitHub release JSON document, or null when it isn't an object or its tag is not a version.</summary>
    public static UpdateInfo? Parse(string json, string repository = DefaultRepository)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Str(root, "tag_name") is not { } tag || !TryParseVersion(tag, out var version)) return null;
        string? url = null;
        long size = 0;
        string? sha256 = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
                if (Str(asset, "name") is { } name && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)
                    && Str(asset, "browser_download_url") is { } link)
                {
                    url = link;
                    size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
                    sha256 = Sha256Of(Str(asset, "digest"));
                    break;
                }
        return new UpdateInfo(version, tag,
            Str(root, "name") is { Length: > 0 } title ? title : tag,
            Str(root, "body"),
            Str(root, "html_url") ?? $"https://github.com/{repository}/releases/latest",
            url, size, sha256);
    }

    // GitHub's asset digest looks like "sha256:<64 hex digits>".
    static string? Sha256Of(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[prefix.Length..];
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : null;
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>"v1.2.3", "1.2", "V2.0.0-beta+abc" read as 1.2.3, 1.2.0, 2.0.0: a leading v and any -/+ suffix are ignored, missing parts are 0.</summary>
    public static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var cut = t.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) t = t[..cut];
        if (!t.Contains('.')) t += ".0";
        if (!Version.TryParse(t, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>major.minor.patch only, so 0.1.0 and 0.1.0.0 compare equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
