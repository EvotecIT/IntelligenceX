using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    private static void MapWakes(WebApplication app, CoordinationStore store) {
        app.MapPost(Prefix + "/wakes", async (CoordinationWakeRequest wake, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.Wake(wake, Caller(http)), DateTimeOffset.UtcNow, ct)));
        app.MapGet(Prefix + "/wakes", async (long? afterOrder, HttpContext http, CancellationToken ct) =>
            Results.Ok(await store.ExecuteAsync((engine, _) => engine.WakesToDeliver(Caller(http), Math.Max(0, afterOrder ?? 0)),
                DateTimeOffset.UtcNow, ct)));
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

    protected override async Task ExecuteAsync(CancellationToken stop) {
        while (!stop.IsCancellationRequested) {
            try { await PassAsync(stop).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) {
                Console.Error.WriteLine("wake-watch-failed " + error.GetType().Name);
            }
            await Task.Delay(Interval, stop).ConfigureAwait(false);
        }
    }

    private async Task PassAsync(CancellationToken stop) {
        // Timed wakes and expiry need no outside read; applying them here keeps them on time when nothing else is happening.
        var watched = await store.ExecuteAsync((engine, _) => { engine.ApplyWakes(); return engine.WatchedPullRequests(); },
            DateTimeOffset.UtcNow, stop).ConfigureAwait(false);
        foreach (var (repo, pr) in watched) {
            var read = await readiness.ReadAsync(repo, pr, stop).ConfigureAwait(false);
            if (Outcome(read) is not { } outcome) continue;
            await store.ExecuteAsync((engine, _) => engine.ChecksFinished(repo, pr, outcome), DateTimeOffset.UtcNow, stop).ConfigureAwait(false);
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
            "green" => "The checks on " + name + head + " passed.",
            "failing" when read.PendingCheckCount == 0 => "The checks on " + name + head + " finished with " + read.FailingCheckCount +
                " failing: " + string.Join(", ", read.FailingChecks) + (read.FailingCheckCount > read.FailingChecks.Length ? ", ..." : "") + ".",
            _ => null
        };
    }
}
