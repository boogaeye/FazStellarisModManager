using System.Diagnostics;

namespace FazStellarisModmanager.Tests.TestUtil;

public static class Wait
{
    /// <summary>Polls until the condition holds (network events arrive asynchronously).</summary>
    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"Timed out waiting for: {what}");
            await Task.Delay(20);
        }
    }
}
