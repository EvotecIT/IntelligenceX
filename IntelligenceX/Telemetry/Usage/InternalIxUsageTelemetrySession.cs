using System;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Attaches persistent runtime usage telemetry to an <see cref="IntelligenceXClient"/>.
/// </summary>
public sealed class InternalIxUsageTelemetrySession : IDisposable {
#if !NETSTANDARD2_0
    private readonly SqliteSourceRootStore _sourceRootStore;
    private readonly SqliteUsageEventStore _usageEventStore;
    private readonly InternalIxUsageRecorder _recorder;
#endif
    private bool _disposed;

#if !NETSTANDARD2_0
    private InternalIxUsageTelemetrySession(
        SqliteSourceRootStore sourceRootStore,
        SqliteUsageEventStore usageEventStore,
        InternalIxUsageRecorder recorder) {
        _sourceRootStore = sourceRootStore;
        _usageEventStore = usageEventStore;
        _recorder = recorder;
    }
#else
    private InternalIxUsageTelemetrySession() {
    }
#endif

    /// <summary>
    /// Attempts to create and attach a telemetry persistence session for the supplied client.
    /// </summary>
    public static InternalIxUsageTelemetrySession? TryCreate(IntelligenceXClient client, IntelligenceXClientOptions options) {
        if (client is null) {
            throw new ArgumentNullException(nameof(client));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var dbPath = UsageTelemetryPathResolver.ResolveDatabasePath(
            options.UsageTelemetryDatabasePath,
            options.EnableUsageTelemetry);
        if (string.IsNullOrWhiteSpace(dbPath)) {
            return null;
        }

#if NETSTANDARD2_0
        throw new NotSupportedException("Persistent usage telemetry requires a framework with SQLite support.");
#else
        SqliteSourceRootStore? sourceRootStore = null;
        SqliteUsageEventStore? usageEventStore = null;
        InternalIxUsageRecorder? recorder = null;
        try {
            sourceRootStore = new SqliteSourceRootStore(dbPath!);
            usageEventStore = new SqliteUsageEventStore(dbPath!);
            recorder = new InternalIxUsageRecorder(
                client,
                sourceRootStore,
                usageEventStore,
                providerId: ResolveTelemetryProviderId(client, options),
                machineId: options.UsageTelemetryMachineId,
                accountLabel: options.UsageTelemetryAccountLabel,
                providerAccountId: ResolveProviderAccountId(options),
                sourcePath: options.UsageTelemetrySourcePath);
            return new InternalIxUsageTelemetrySession(sourceRootStore, usageEventStore, recorder);
        } catch {
            recorder?.Dispose();
            usageEventStore?.Dispose();
            sourceRootStore?.Dispose();
            throw;
        }
#endif
    }

#if !NETSTANDARD2_0
    private static string ResolveTelemetryProviderId(IntelligenceXClient client, IntelligenceXClientOptions options) {
        switch (client.TransportKind) {
            case OpenAITransportKind.Native:
                return "chatgpt";
            case OpenAITransportKind.AppServer:
                return "codex";
            case OpenAITransportKind.CopilotCli:
                return "copilot";
            case OpenAITransportKind.CompatibleHttp:
                return OpenAICompatibleHttpProviderDetector.InferTelemetryProviderId(options.CompatibleHttpOptions.BaseUrl)
                       ?? InternalIxUsageRecorder.StableProviderId;
            default:
                return InternalIxUsageRecorder.StableProviderId;
        }
    }

    private static string? ResolveProviderAccountId(IntelligenceXClientOptions options) {
        return NormalizeOptional(options.UsageTelemetryProviderAccountId) ??
               NormalizeOptional(options.NativeOptions.AuthAccountId);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
#endif

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
#if !NETSTANDARD2_0
        _recorder.Dispose();
        _usageEventStore.Dispose();
        _sourceRootStore.Dispose();
#endif
    }
}
