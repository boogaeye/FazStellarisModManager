namespace FazStellarisModmanager.Core.Workshop;

/// <summary>
/// The helper process's side of <see cref="WorkshopHelperProtocol"/>: reads the request, runs
/// <see cref="IWorkshopService.InstallAsync"/> and writes its progress and results. The end of standard input (the app
/// closed it, or the app itself is gone) cancels the install. Returns the process exit code.
/// </summary>
public static class WorkshopHelperHost
{
    public static async Task<int> RunAsync(IWorkshopService service, TextReader input, TextWriter output)
    {
        if (WorkshopHelperProtocol.ParseRequest(input.ReadLine()) is not { } ids) return WorkshopHelperProtocol.ExitBadRequest;
        if (ids.Count == 0)
        {
            // Nothing to do, so don't connect to Steam at all.
            output.WriteLine(WorkshopHelperProtocol.Write(new HelperResults([])));
            output.Flush();
            return WorkshopHelperProtocol.ExitOk;
        }

        using var cts = new CancellationTokenSource();
        // A blocking read on its own background thread: console input has no truly asynchronous read.
        new Thread(() =>
        {
            try { while (input.ReadLine() is not null) { } }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* the install already finished */ }
        }) { IsBackground = true, Name = "Workshop helper input" }.Start();

        var gate = new Lock();
        void Send(HelperMessage message)
        {
            lock (gate)
            {
                output.WriteLine(WorkshopHelperProtocol.Write(message));
                output.Flush();
            }
        }

        try
        {
            var results = await service.InstallAsync(ids, new LineProgress(Send), cts.Token);
            Send(new HelperResults(results));
            return WorkshopHelperProtocol.ExitOk;
        }
        catch (WorkshopUnavailableException ex)
        {
            Send(new HelperUnavailable(ex.Message));
            return WorkshopHelperProtocol.ExitUnavailable;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return WorkshopHelperProtocol.ExitCancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Send(new HelperUnavailable($"The Steam helper failed ({ex.Message})."));
            return WorkshopHelperProtocol.ExitFailed;
        }
    }

    // Writes each report immediately, in order (Progress<T> would post to the thread pool and could reorder them).
    sealed class LineProgress(Action<HelperMessage> send) : IProgress<WorkshopProgress>
    {
        public void Report(WorkshopProgress value) => send(new HelperProgress(value));
    }
}
