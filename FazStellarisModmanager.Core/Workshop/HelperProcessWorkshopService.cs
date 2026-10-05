using System.Diagnostics;
using System.Text;

namespace FazStellarisModmanager.Core.Workshop;

/// <summary>
/// Workshop access with every Steam client call made in a short-lived helper process (see
/// <see cref="WorkshopHelperProtocol"/>). Steam treats the process that connected as the running game until that process
/// exits, so connecting from the app itself would keep Stellaris "running" until the app is closed; the helper exits as
/// soon as its downloads finish. <see cref="GetInfoAsync"/> uses Steam's public Web API in-process.
/// <para>
/// Cancelling closes the helper's standard input, which makes it cancel and answer; a helper still running after
/// <c>cancelGrace</c> is killed. Calls are serialised.
/// </para>
/// </summary>
public sealed class HelperProcessWorkshopService(Func<ProcessStartInfo> helper, HttpClient? http = null, TimeSpan? cancelGrace = null) : IWorkshopService
{
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    readonly TimeSpan _cancelGrace = cancelGrace ?? TimeSpan.FromSeconds(5);
    volatile bool _active;

    /// <summary>True while the helper process runs.</summary>
    public bool IsActive => _active;

    /// <summary>Titles and download sizes from Steam's public Web API; no helper, no Steam client connection.</summary>
    public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) =>
        WorkshopWebApi.GetDetailsAsync(_http, ids, ct);

    /// <summary>
    /// Runs the install in the helper. Returns the helper's results whenever it gave them (even after a cancel or a
    /// failing exit); otherwise throws <see cref="OperationCanceledException"/> when cancelled, or
    /// <see cref="WorkshopUnavailableException"/> with the helper's message, or naming its exit code when it said nothing.
    /// </summary>
    public async Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await RunHelperAsync(ids, progress, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<IReadOnlyList<WorkshopItemResult>> RunHelperAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        using var process = new Process();
        try
        {
            var start = helper();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.StandardInputEncoding = new UTF8Encoding(false);
            start.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo = start;
            process.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new WorkshopUnavailableException($"Could not start the Steam helper ({ex.Message}).", ex);
        }

        _active = true;
        try
        {
            try
            {
                await process.StandardInput.WriteLineAsync(WorkshopHelperProtocol.WriteRequest(ids));
                await process.StandardInput.FlushAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                // The helper is already gone; its output and exit code say why.
            }

            using var killTimer = new CancellationTokenSource();
            using var kill = killTimer.Token.Register(() => KillQuietly(process));
            using var onCancel = ct.Register(() =>
            {
                CloseQuietly(process.StandardInput);
                killTimer.CancelAfter(_cancelGrace);
            });

            HelperResults? results = null;
            HelperUnavailable? unavailable = null;
            // Not cancellable on purpose: after a cancel the helper still answers, or is killed, and either ends the output.
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
            {
                switch (WorkshopHelperProtocol.Parse(line))
                {
                    case HelperProgress p: progress.Report(p.Progress); break;
                    case HelperResults r: results ??= r; break;
                    case HelperUnavailable u: unavailable ??= u; break;
                }
            }
            await process.WaitForExitAsync(CancellationToken.None);

            if (results is not null) return results.Items;
            ct.ThrowIfCancellationRequested();
            if (unavailable is not null) throw new WorkshopUnavailableException(unavailable.Message);
            throw new WorkshopUnavailableException($"The Steam helper stopped without an answer (exit code {process.ExitCode}).");
        }
        finally
        {
            KillQuietly(process);
            _active = false;
        }
    }

    static void CloseQuietly(StreamWriter input)
    {
        try { input.Close(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* the helper is already gone */ }
    }

    static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already exited (or never started).
        }
    }
}
