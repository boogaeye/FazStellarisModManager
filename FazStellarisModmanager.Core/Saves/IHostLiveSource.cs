using FazStellarisModmanager.Core.Session;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>Live data received from a session host (implemented by SessionService).</summary>
public interface IHostLiveSource
{
    /// <summary>The last update from the host; kept after the connection ends, until ForgetHostLive or a new join.</summary>
    LiveUpdate? HostLive { get; }
    DateTime? HostLiveReceivedUtc { get; }
    /// <summary>True while connected to a host as a client.</summary>
    bool IsClient { get; }
    string? HostName { get; }
    event Action? Changed;
    Task ViewAsAsync(int countryId);
    /// <summary>Drops the kept host data (ignored while connected as a client).</summary>
    void ForgetHostLive();
}
