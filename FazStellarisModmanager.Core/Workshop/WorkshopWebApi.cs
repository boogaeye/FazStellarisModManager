using System.Globalization;
using System.Text.Json;

namespace FazStellarisModmanager.Core.Workshop;

/// <summary>
/// Workshop item details from Steam's public Web API (ISteamRemoteStorage/GetPublishedFileDetails). Needs no key and no
/// running Steam client, so the app does not show up as playing Stellaris while it looks items up.
/// </summary>
public static class WorkshopWebApi
{
    public const string DetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// One entry per id, in the order given; ids Steam doesn't know (or hides) have a null title and size.
    /// Throws <see cref="WorkshopUnavailableException"/> on network failures, timeouts (15 s) and unreadable answers;
    /// <see cref="OperationCanceledException"/> only when <paramref name="ct"/> is cancelled.
    /// </summary>
    public static async Task<IReadOnlyList<WorkshopItemInfo>> GetDetailsAsync(HttpClient http, IReadOnlyList<ulong> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        string json;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, DetailsUrl) { Content = new FormUrlEncodedContent(Form(ids)) };
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new WorkshopUnavailableException($"Steam's Workshop service answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            json = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new WorkshopUnavailableException("Steam's Workshop service did not answer in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new WorkshopUnavailableException($"Could not reach Steam's Workshop service ({ex.Message}).", ex);
        }
        var byId = new Dictionary<ulong, WorkshopItemInfo>();
        foreach (var item in ParseDetails(json)) byId.TryAdd(item.Id, item);
        return ids.Select(id => byId.GetValueOrDefault(id) ?? new WorkshopItemInfo(id, null, null)).ToList();
    }

    /// <summary>The form fields: itemcount=N and publishedfileids[i]=id.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Form(IReadOnlyList<ulong> ids)
    {
        yield return new("itemcount", ids.Count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < ids.Count; i++)
            yield return new($"publishedfileids[{i}]", ids[i].ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The items in a GetPublishedFileDetails answer. An item whose result isn't 1 (OK) or that has no title is unknown
    /// (null title and size). Throws <see cref="WorkshopUnavailableException"/> when the document can't be read.
    /// </summary>
    public static IReadOnlyList<WorkshopItemInfo> ParseDetails(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("publishedfiledetails", out var details) || details.ValueKind != JsonValueKind.Array)
                throw new WorkshopUnavailableException("Steam's Workshop answer could not be read.");
            var list = new List<WorkshopItemInfo>();
            foreach (var entry in details.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || Number(entry, "publishedfileid") is not { } rawId || rawId <= 0) continue;
                var id = (ulong)rawId;
                var ok = Number(entry, "result") == 1;
                var title = ok && entry.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } s ? s : null;
                list.Add(title is null ? new WorkshopItemInfo(id, null, null) : new WorkshopItemInfo(id, title, Number(entry, "file_size") is >= 0 and var size ? size : null));
            }
            return list;
        }
        catch (JsonException ex)
        {
            throw new WorkshopUnavailableException("Steam's Workshop answer could not be read.", ex);
        }
    }

    // A whole number given either as a JSON number or as a string (Steam sends 64-bit values as strings).
    static long? Number(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }
}
