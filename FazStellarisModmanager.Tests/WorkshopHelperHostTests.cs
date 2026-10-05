using FazStellarisModmanager.Core.Workshop;
using FazStellarisModmanager.Tests.TestUtil;

namespace FazStellarisModmanager.Tests;

public class WorkshopHelperHostTests
{
    /// <summary>Standard input as the app holds it: the request line, then nothing until the app closes it.</summary>
    sealed class HeldInput(string request) : TextReader
    {
        readonly SemaphoreSlim _closed = new(0);
        bool _sentRequest;

        public void Close_() => _closed.Release();

        public override string? ReadLine()
        {
            if (!_sentRequest)
            {
                _sentRequest = true;
                return request;
            }
            _closed.Wait();
            return null;
        }
    }

    sealed class ThrowingWorkshop(Exception ex) : IWorkshopService
    {
        public bool IsActive => false;
        public Task<IReadOnlyList<WorkshopItemInfo>> GetInfoAsync(IReadOnlyList<ulong> ids, CancellationToken ct) => throw ex;
        public Task<IReadOnlyList<WorkshopItemResult>> InstallAsync(IReadOnlyList<ulong> ids, IProgress<WorkshopProgress> progress, CancellationToken ct) => throw ex;
    }

    static List<HelperMessage?> Lines(StringWriter output) =>
        output.ToString().Split((char)10, StringSplitOptions.RemoveEmptyEntries).Select(l => WorkshopHelperProtocol.Parse(l.TrimEnd((char)13))).ToList();

    [Fact]
    public async Task Installs_the_requested_ids_writing_progress_then_results()
    {
        var fake = new FakeWorkshop(id => id != 2);
        var input = new HeldInput(WorkshopHelperProtocol.WriteRequest([1, 2]));
        var output = new StringWriter();

        var code = await WorkshopHelperHost.RunAsync(fake, input, output);
        input.Close_();

        Assert.Equal(WorkshopHelperProtocol.ExitOk, code);
        Assert.Equal([1UL, 2UL], fake.Requested);
        var lines = Lines(output);
        Assert.All(lines, Assert.NotNull);
        Assert.Equal(
            [new WorkshopProgress(1, WorkshopItemState.Installed, 1), new WorkshopProgress(2, WorkshopItemState.Failed, 0)],
            lines.SkipLast(1).Select(l => Assert.IsType<HelperProgress>(l).Progress));
        Assert.Equal(
            [new WorkshopItemResult(1, true, "folder", null), new WorkshopItemResult(2, false, null, "Not available.")],
            Assert.IsType<HelperResults>(lines[^1]).Items);
    }

    [Fact]
    public async Task Steam_being_unavailable_is_reported_as_a_message_and_exit_code()
    {
        var input = new HeldInput(WorkshopHelperProtocol.WriteRequest([1]));
        var output = new StringWriter();

        var code = await WorkshopHelperHost.RunAsync(new ThrowingWorkshop(new WorkshopUnavailableException("Steam is not running.")), input, output);
        input.Close_();

        Assert.Equal(WorkshopHelperProtocol.ExitUnavailable, code);
        Assert.Equal("Steam is not running.", Assert.IsType<HelperUnavailable>(Assert.Single(Lines(output))).Message);
    }

    [Fact]
    public async Task An_unexpected_failure_is_reported_as_unavailable_with_the_failed_exit_code()
    {
        var input = new HeldInput(WorkshopHelperProtocol.WriteRequest([1]));
        var output = new StringWriter();

        var code = await WorkshopHelperHost.RunAsync(new ThrowingWorkshop(new InvalidOperationException("boom")), input, output);
        input.Close_();

        Assert.Equal(WorkshopHelperProtocol.ExitFailed, code);
        Assert.Contains("boom", Assert.IsType<HelperUnavailable>(Assert.Single(Lines(output))).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task A_bad_request_installs_nothing(string? request)
    {
        var fake = new FakeWorkshop(_ => true);
        var output = new StringWriter();

        var code = await WorkshopHelperHost.RunAsync(fake, new StringReader(request ?? ""), output);

        Assert.Equal(WorkshopHelperProtocol.ExitBadRequest, code);
        Assert.Empty(fake.Requested);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task An_empty_request_answers_without_calling_the_service()
    {
        var output = new StringWriter();

        var code = await WorkshopHelperHost.RunAsync(new ThrowingWorkshop(new InvalidOperationException("called")), new StringReader(WorkshopHelperProtocol.WriteRequest([])), output);

        Assert.Equal(WorkshopHelperProtocol.ExitOk, code);
        Assert.Empty(Assert.IsType<HelperResults>(Assert.Single(Lines(output))).Items);
    }

    [Fact]
    public async Task Closing_standard_input_cancels_the_install()
    {
        var started = new TaskCompletionSource();
        var fake = new FakeWorkshop(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return true;
        });
        var input = new HeldInput(WorkshopHelperProtocol.WriteRequest([1]));
        var output = new StringWriter();

        var run = WorkshopHelperHost.RunAsync(fake, input, output);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        input.Close_();
        var code = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WorkshopHelperProtocol.ExitCancelled, code);
        Assert.DoesNotContain(Lines(output), l => l is HelperResults);
    }
}
