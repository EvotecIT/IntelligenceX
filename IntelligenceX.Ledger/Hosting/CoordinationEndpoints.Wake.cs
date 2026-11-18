using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static void MapWakes(WebApplication app, CoordinationStore store) {
        app.MapPost(Prefix + "/wakes", async (CoordinationWakeRequest wake, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.Wake(wake, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/wakes", async (long? afterOrder, string? sessionId, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.WakesToDeliver(Caller(http), Math.Max(0, afterOrder ?? 0),
                string.IsNullOrEmpty(sessionId) ? null : sessionId), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/wakes/{id}", async (string id, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.WakeStatus(id, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/wakes/{id}/cancel", async (string id, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.CancelWake(id, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapPost(Prefix + "/wakes/{id}/delivery", async (string id, CoordinationWakeDeliveryCommand command, HttpContext http,
            CancellationToken ct) => Results.Ok(await store.ExecuteAsync((engine, _) => engine.RecordWakeDelivery(id, command, Caller(http)),
                DateTimeOffset.UtcNow, ct)));
    }
}

/// <summary>Watches the pull requests that waiting wakes name, with the same cached GitHub read as pr_ready, and makes
/// their wakes due once the checks on the current head have finished or the pull request has closed. No model runs.</summary>
internal sealed class WakeWatcher(CoordinationStore store, PullRequestReadinessService readiness) : BackgroundService {
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>Timers and expiry run on their own loop, so slow GitHub reads never hold back a timed wake.</summary>
    internal static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(15);

    protected override Task ExecuteAsync(CancellationToken stop) => Task.WhenAll(
        LoopAsync(TimerInterval, () => store.ExecuteAsync((engine, _) => { engine.ApplyWakes(); return 0; }, DateTimeOffset.UtcNow, stop), stop),
        LoopAsync(Interval, () => ChecksAsync(stop), stop));

    private static async Task LoopAsync(TimeSpan interval, Func<Task> pass, CancellationToken stop) {
        while (!stop.IsCancellationRequested) {
            try { await pass().ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) {
                Console.Error.WriteLine("wake-watch-failed " + error.GetType().Name);
            }
            await Task.Delay(interval, stop).ConfigureAwait(false);
        }
    }

    private async Task ChecksAsync(CancellationToken stop) {
        var watched = await store.ExecuteAsync((engine, _) => engine.WatchedPullRequests(), DateTimeOffset.UtcNow, stop).ConfigureAwait(false);
        foreach (var (repo, pr) in watched) {
            var read = await readiness.ReadAsync(repo, pr, stop).ConfigureAwait(false);
            if (Outcome(read) is not { } outcome) continue;
            // A cached read covers only the wakes that existed when it was made.
            await store.ExecuteAsync((engine, _) => engine.ChecksFinished(repo, pr, outcome, read.CheckedAtUtc), DateTimeOffset.UtcNow, stop)
                .ConfigureAwait(false);
        }
    }

    /// <summary>What a wake reports once there is something to act on, or null while the checks are still running or the
    /// read was incomplete. A head that moved during the read is read again on the next pass.</summary>
    internal static string? Outcome(PullRequestReadiness read) {
        var name = read.Repo + "#" + read.Pr;
        if (read.State is "merged" or "closed") return name + " was " + read.State + ".";
        if (read.State != "open" || read.HeadSha is null || read.ChecksHeadSha is not null || !read.RecheckedAfterObservedHead) return null;
        var head = " on " + read.HeadSha[..Math.Min(7, read.HeadSha.Length)];
        return read.Checks switch {
            // Green with no checks reported means none have started yet on a fresh head, not that they passed.
            "green" when read.CheckCount > 0 => "The checks on " + name + head + " passed.",
            "failing" when read.PendingCheckCount == 0 => "The checks on " + name + head + " finished with " + read.FailingCheckCount +
                " failing: " + string.Join(", ", read.FailingChecks) + (read.FailingCheckCount > read.FailingChecks.Length ? ", ..." : "") + ".",
            _ => null
        };
    }
}
