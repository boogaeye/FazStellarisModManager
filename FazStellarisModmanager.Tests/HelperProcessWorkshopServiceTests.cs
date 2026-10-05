using System.Diagnostics;
using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

/// <summary>Runs the process plumbing against fake helpers written as cmd.exe one-liners.</summary>
public class HelperProcessWorkshopServiceTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    sealed class Collect : IProgress<WorkshopProgress>
    {
        public List<WorkshopProgress> Reports { get; } = [];
        public void Report(WorkshopProgress value) { lock (Reports) Reports.Add(value); }
    }

    // A fake helper: the script runs in the temp dir, where out.txt holds the lines it prints.
    HelperProcessWorkshopService Service(string script, params string[] output)
    {
        File.WriteAllLines(Path.Combine(_dir.Path, "out.txt"), output);
        return new HelperProcessWorkshopService(
            () => new ProcessStartInfo("cmd.exe", "/v:on /c " + '"' + script + '"') { WorkingDirectory = _dir.Path },
            cancelGrace: TimeSpan.FromMilliseconds(300));
    }

    static readonly WorkshopItemResult[] Results = [new(1, true, @"C:\Steam\workshop\content\281990\1", null), new(2, false, null, "The download stalled.")];

    static string Line(HelperMessage m) => WorkshopHelperProtocol.Write(m);

    [Fact]
    public async Task Sends_the_request_reports_progress_in_order_and_returns_the_results()
    {
        var service = Service("set /p REQ=&(echo !REQ!)>req.txt&type out.txt",
            "[S_API] SteamAPI_Init(): Loaded local 'steamclient64.dll' OK.",
            Line(new HelperProgress(new WorkshopProgress(1, WorkshopItemState.Waiting, 0))),
            Line(new HelperProgress(new WorkshopProgress(1, WorkshopItemState.Downloading, 0.5))),
            "not a message",
            Line(new HelperProgress(new WorkshopProgress(2, WorkshopItemState.Failed, 0, "The download stalled."))),
            Line(new HelperResults(Results)));
        var progress = new Collect();

        var results = await service.InstallAsync([1, 2], progress, CancellationToken.None);

        Assert.Equal(Results, results);
        Assert.Equal(
            [new WorkshopProgress(1, WorkshopItemState.Waiting, 0), new WorkshopProgress(1, WorkshopItemState.Downloading, 0.5),
             new WorkshopProgress(2, WorkshopItemState.Failed, 0, "The download stalled.")],
            progress.Reports);
        Assert.Equal(WorkshopHelperProtocol.WriteRequest([1, 2]), File.ReadAllText(Path.Combine(_dir.Path, "req.txt")).Trim());
        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task Results_count_even_when_the_helper_then_exits_with_an_error()
    {
        var service = Service("type out.txt&exit 5", Line(new HelperResults(Results)));

        Assert.Equal(Results, await service.InstallAsync([1, 2], new Collect(), CancellationToken.None));
    }

    [Fact]
    public async Task An_unavailable_message_becomes_WorkshopUnavailableException()
    {
        var service = Service("type out.txt&exit 2", Line(new HelperUnavailable("Could not connect to Steam.")));

        var ex = await Assert.ThrowsAsync<WorkshopUnavailableException>(() => service.InstallAsync([1], new Collect(), CancellationToken.None));

        Assert.Equal("Could not connect to Steam.", ex.Message);
        Assert.False(service.IsActive);
    }

    [Theory]
    [InlineData("exit 1", "1")]
    [InlineData("exit 0", "0")]
    [InlineData("type out.txt&exit 3", "3")]
    public async Task A_helper_that_ends_without_an_answer_is_unavailable_naming_its_exit_code(string script, string code)
    {
        var service = Service(script, "garbage", Line(new HelperProgress(new WorkshopProgress(1, WorkshopItemState.Waiting, 0))));

        var ex = await Assert.ThrowsAsync<WorkshopUnavailableException>(() => service.InstallAsync([1], new Collect(), CancellationToken.None));

        Assert.Contains($"exit code {code}", ex.Message);
    }

    [Fact]
    public async Task A_helper_that_cannot_start_is_unavailable()
    {
        var service = new HelperProcessWorkshopService(() => new ProcessStartInfo(Path.Combine(_dir.Path, "missing.exe")));

        await Assert.ThrowsAsync<WorkshopUnavailableException>(() => service.InstallAsync([1], new Collect(), CancellationToken.None));
        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task No_ids_start_no_helper()
    {
        var service = new HelperProcessWorkshopService(() => throw new InvalidOperationException("started"));

        Assert.Empty(await service.InstallAsync([], new Collect(), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_closes_standard_input_so_the_helper_can_answer()
    {
        // The second "set /p" returns once standard input is closed, like the real helper cancelling its install.
        WorkshopItemResult[] cancelled = [new(1, false, null, "Cancelled.")];
        var service = Service("set /p REQ=&set /p X=&type out.txt", Line(new HelperResults(cancelled)));
        using var cts = new CancellationTokenSource();

        var install = service.InstallAsync([1], new Collect(), cts.Token);
        await Wait.Until(() => service.IsActive, "helper running");
        cts.Cancel();

        Assert.Equal(cancelled, await install.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task Cancelling_kills_a_helper_that_does_not_stop()
    {
        var service = Service("ping -n 60 127.0.0.1 >nul");
        using var cts = new CancellationTokenSource();

        var install = service.InstallAsync([1], new Collect(), cts.Token);
        await Wait.Until(() => service.IsActive, "helper running");
        await Task.Delay(200);
        Assert.False(install.IsCompleted);
        var clock = Stopwatch.StartNew();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"took {clock.Elapsed}");
        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task Is_active_only_while_the_helper_runs()
    {
        var service = Service("set /p REQ=&set /p X=&type out.txt", Line(new HelperResults([])));
        using var cts = new CancellationTokenSource();
        Assert.False(service.IsActive);

        var install = service.InstallAsync([1], new Collect(), cts.Token);
        await Wait.Until(() => service.IsActive, "helper running");
        await Task.Delay(200);
        Assert.True(service.IsActive);
        cts.Cancel();
        await install.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(service.IsActive);
    }
}
