using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FazStellarisModmanager.Services;

/// <summary>
/// This PC's public IPv4 address as the internet sees it (from api.ipify.org), for the host's share chip. The first answer
/// is kept for the rest of the run; a failed lookup is tried again next time.
/// </summary>
public sealed class PublicAddressLookup
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 1024 };

    readonly SemaphoreSlim _gate = new(1, 1);
    volatile string? _address;

    /// <summary>The address found by an earlier lookup (null until one succeeds).</summary>
    public string? Known => _address;

    /// <summary>The public IPv4 address, or null when the lookup failed, timed out, was cancelled or did not return an IPv4 address.</summary>
    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        if (_address is { } known) return known;
        try
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (_address is { } again) return again;
                var text = (await Http.GetStringAsync("https://api.ipify.org", ct)).Trim();
                // IPAddress.TryParse also takes short forms such as "1" (0.0.0.1): require the four dotted parts.
                if (text.Count(c => c == '.') == 3 && IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                    _address = ip.ToString();
                return _address;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }
}
