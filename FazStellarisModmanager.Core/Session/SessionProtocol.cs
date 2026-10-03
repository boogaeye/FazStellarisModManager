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

    /// <summary>"host" or "host:port". Throws ArgumentException with a user-facing message.</summary>
    public static (string Host, int Port) ParseAddress(string input)
    {
        var s = input.Trim();
        if (s.Length == 0) throw new ArgumentException("Enter the host's address.");
        var colon = s.LastIndexOf(':');
        if (colon < 0) return (s, DefaultPort);
        var host = s[..colon].Trim();
        if (host.Length == 0 || !int.TryParse(s[(colon + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"'{input.Trim()}' is not a valid address. Use host or host:port.");
        return (host, port);
    }

    public static PlayerStatus StatusFor(DiffResult d) =>
        !d.IsReliable ? PlayerStatus.Unreliable : d.IsMatch ? PlayerStatus.Ready : PlayerStatus.Mismatch;

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
