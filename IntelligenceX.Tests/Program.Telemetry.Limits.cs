using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestProviderLimitSnapshotServiceBatchKeepsHealthyProvidersWhenOneFails() {
        var service = new ProviderLimitSnapshotService((providerId, cancellationToken) => providerId switch {
            "codex" => Task.FromResult(
                new ProviderLimitSnapshot(
                    "codex",
                    "Codex",
                    "test",
                    null,
                    null,
                    Array.Empty<ProviderLimitWindow>(),
                    "healthy",
                    null,
                    DateTimeOffset.UtcNow)),
            "claude" => Task.FromException<ProviderLimitSnapshot>(new InvalidOperationException("boom")),
            _ => Task.FromException<ProviderLimitSnapshot>(new ArgumentOutOfRangeException(nameof(providerId)))
        });

        var snapshots = service.FetchAsync(new[] { "codex", "claude" }).GetAwaiter().GetResult();

        AssertEqual(2, snapshots.Count, "limits batch snapshot count");
        AssertEqual("healthy", snapshots["codex"].Summary ?? string.Empty, "limits batch keeps healthy provider snapshot");
        AssertContainsText(snapshots["claude"].DetailMessage ?? string.Empty, "boom", "limits batch captures failed provider detail");
    }

    private static void TestProviderLimitSnapshotServiceBatchPropagatesCallerCancellation() {
        var canceled = new CancellationToken(canceled: true);
        var service = new ProviderLimitSnapshotService((providerId, cancellationToken) => Task.FromCanceled<ProviderLimitSnapshot>(cancellationToken));

        AssertThrows<OperationCanceledException>(
            () => service.FetchAsync(new[] { "codex" }, canceled).GetAwaiter().GetResult(),
            "limits batch propagates caller cancellation");
    }
}
