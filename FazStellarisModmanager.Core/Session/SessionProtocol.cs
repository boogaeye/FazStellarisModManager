using System.Globalization;
using System.Net;
using System.Net.Sockets;
using FazStellarisModmanager.Core.Diff;

namespace FazStellarisModmanager.Core.Session;

public static class SessionProtocol
{
    /// <summary>Bump when messages change incompatibly; hosts reject clients with a different version.</summary>
    public const int Version = 1;
    public const int DefaultPort = 27015;

    public static string AppVersion => typeof(SessionProtocol).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>"host", "host:port", "[ipv6]" or "[ipv6]:port". Throws ArgumentException with a user-facing message.</summary>
    public static (string Host, int Port) ParseAddress(string input)
    {
        var s = input.Trim();
        if (s.Length == 0) throw new ArgumentException("Enter the host's address.");
        var bad = new ArgumentException($"'{s}' is not a valid address. Use host or host:port.");
        if (s.Contains("://", StringComparison.Ordinal)) throw bad;

        string host;
        string? portText;
        if (s[0] == '[')
        {
            var close = s.IndexOf(']');
            if (close < 0) throw bad;
            host = s[1..close].Trim();
            var rest = s[(close + 1)..];
            if (rest.Length == 0) portText = null;
            else if (rest[0] == ':') portText = rest[1..];
            else throw bad;
        }
        else if (s.IndexOf(':') != s.LastIndexOf(':'))
        {
            // More than one ':' without brackets: only a bare IPv6 address is acceptable.
            if (IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6) return (s, DefaultPort);
            throw bad;
        }
        else
        {
            var colon = s.LastIndexOf(':');
            if (colon < 0) { host = s; portText = null; }
            else { host = s[..colon].Trim(); portText = s[(colon + 1)..]; }
        }

        if (host.Length == 0) throw bad;
        if (portText is null) return (host, DefaultPort);
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535) throw bad;
        return (host, port);
    }

    /// <summary>Mismatch beats Unreliable: a known difference is real even if the scan had warnings.</summary>
    public static PlayerStatus StatusFor(DiffResult d) =>
        !d.IsMatch ? PlayerStatus.Mismatch : !d.IsReliable ? PlayerStatus.Unreliable : PlayerStatus.Ready;

    /// <summary>This PC's IPv4 addresses, to tell friends where to connect.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch (SocketException)
        {
            return [];
        }
    }
}
