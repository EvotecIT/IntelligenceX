using System;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private static string NormalizeStartupMetadataSyncPhase(string? phase) {
        var normalized = (phase ?? string.Empty).Trim();
        return normalized.Length == 0 ? "syncing startup metadata" : normalized;
    }

    private void BeginStartupMetadataSyncTracking(string phase) {
        var normalizedPhase = NormalizeStartupMetadataSyncPhase(phase);
        lock (_startupMetadataSyncLock) {
            _startupMetadataSyncStartedUtcTicks = DateTime.UtcNow.Ticks;
            _startupMetadataSyncPhase = normalizedPhase;
            Volatile.Write(ref _startupMetadataSyncInProgress, 1);
        }
    }

    private void UpdateStartupMetadataSyncPhase(string phase) {
        var normalizedPhase = NormalizeStartupMetadataSyncPhase(phase);
        lock (_startupMetadataSyncLock) {
            if (Volatile.Read(ref _startupMetadataSyncInProgress) == 0) {
                _startupMetadataSyncStartedUtcTicks = DateTime.UtcNow.Ticks;
                Volatile.Write(ref _startupMetadataSyncInProgress, 1);
            }

            _startupMetadataSyncPhase = normalizedPhase;
        }
    }

    private void EndStartupMetadataSyncTracking() {
        lock (_startupMetadataSyncLock) {
            Volatile.Write(ref _startupMetadataSyncInProgress, 0);
            _startupMetadataSyncStartedUtcTicks = 0;
            _startupMetadataSyncPhase = string.Empty;
        }
    }

    private bool TryBuildStartupMetadataSyncStatusText(out string statusText) {
        statusText = string.Empty;
        if (Volatile.Read(ref _startupMetadataSyncInProgress) == 0) {
            return false;
        }

        string phase;
        long startedUtcTicks;
        lock (_startupMetadataSyncLock) {
            phase = NormalizeStartupMetadataSyncPhase(_startupMetadataSyncPhase);
            startedUtcTicks = _startupMetadataSyncStartedUtcTicks;
        }

        var elapsed = TimeSpan.Zero;
        if (startedUtcTicks > 0) {
            var startedUtc = new DateTime(startedUtcTicks, DateTimeKind.Utc);
            elapsed = DateTime.UtcNow - startedUtc;
            if (elapsed < TimeSpan.Zero) {
                elapsed = TimeSpan.Zero;
            }
        }

        var elapsedLabel = elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : Math.Max(1, (long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";
        statusText = $"Runtime connected. Startup sync in progress ({phase}, {elapsedLabel}).";
        return true;
    }
}
