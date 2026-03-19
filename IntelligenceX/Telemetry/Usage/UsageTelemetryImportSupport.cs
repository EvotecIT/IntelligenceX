using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IntelligenceX.Telemetry.Usage;

internal static class UsageTelemetryImportSupport {
    public static IReadOnlyList<UsageEventRecord> ImportArtifacts(
        SourceRootRecord root,
        UsageImportContext context,
        string adapterId,
        string parserVersion,
        IEnumerable<string> candidateFiles,
        Func<string, RawArtifactDescriptor, IReadOnlyList<UsageEventRecord>> importFile,
        CancellationToken cancellationToken) {
        var records = new List<UsageEventRecord>();
        foreach (var filePath in candidateFiles) {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = RawArtifactDescriptor.CreateFile(
                root.Id,
                adapterId,
                filePath,
                parserVersion: parserVersion,
                importedAtUtc: context.UtcNow());
            if (ShouldSkipArtifact(context, artifact)) {
                continue;
            }
            if (!context.TryBeginArtifact()) {
                break;
            }

            try {
                var importedRecords = importFile(filePath, artifact);
                foreach (var record in importedRecords) {
                    ApplyResolvedAccount(context, artifact, record);
                    records.Add(record);
                }

                context.RawArtifactStore?.Upsert(artifact);
            } catch (IOException) {
                // Active logs can still be written while providers are running.
                continue;
            } catch (UnauthorizedAccessException) {
                // Synced or partially-migrated roots may transiently deny access.
                continue;
            }
        }

        return DeduplicateImportedRecords(records);
    }

    public static bool ShouldSkipArtifact(UsageImportContext context, RawArtifactDescriptor artifact) {
        if (context.ForceReimport || context.RawArtifactStore is null) {
            return false;
        }

        if (!context.RawArtifactStore.TryGet(artifact.SourceRootId, artifact.AdapterId, artifact.Path, out var existing)) {
            return false;
        }

        return string.Equals(existing.Fingerprint, artifact.Fingerprint, StringComparison.Ordinal) &&
               string.Equals(
                   UsageTelemetryQuickReportSupport.NormalizeOptional(existing.ParserVersion),
                   UsageTelemetryQuickReportSupport.NormalizeOptional(artifact.ParserVersion),
                   StringComparison.OrdinalIgnoreCase);
    }

    public static void ApplyResolvedAccount(
        UsageImportContext context,
        RawArtifactDescriptor artifact,
        UsageEventRecord record) {
        if (context.AccountResolver is null) {
            return;
        }

        var resolvedAccount = context.AccountResolver.Resolve(record, artifact);
        record.ProviderAccountId = UsageTelemetryQuickReportSupport.NormalizeOptional(resolvedAccount.ProviderAccountId) ?? record.ProviderAccountId;
        record.AccountLabel = UsageTelemetryQuickReportSupport.NormalizeOptional(resolvedAccount.AccountLabel) ?? record.AccountLabel;
        record.PersonLabel = UsageTelemetryQuickReportSupport.NormalizeOptional(resolvedAccount.PersonLabel) ?? record.PersonLabel;
    }

    public static void ApplyImportedEventMetadata(
        UsageEventRecord record,
        SourceRootRecord root,
        string? machineId,
        string? providerAccountId = null,
        string? accountLabel = null,
        string? personLabel = null,
        string? sessionId = null,
        string? threadId = null,
        string? turnId = null,
        string? responseId = null,
        string? model = null,
        string? surface = null,
        long? durationMs = null,
        string? rawHash = null,
        UsageTruthLevel truthLevel = UsageTruthLevel.Exact) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }

        record.ProviderAccountId = UsageTelemetryQuickReportSupport.NormalizeOptional(providerAccountId) ?? record.ProviderAccountId;
        record.AccountLabel = UsageTelemetryQuickReportSupport.NormalizeOptional(root.AccountHint)
                              ?? UsageTelemetryQuickReportSupport.NormalizeOptional(accountLabel)
                              ?? record.AccountLabel;
        record.PersonLabel = UsageTelemetryQuickReportSupport.NormalizeOptional(personLabel) ?? record.PersonLabel;
        record.MachineId = UsageTelemetryQuickReportSupport.NormalizeOptional(machineId)
                           ?? UsageTelemetryQuickReportSupport.NormalizeOptional(root.MachineLabel)
                           ?? record.MachineId;
        record.SessionId = UsageTelemetryQuickReportSupport.NormalizeOptional(sessionId) ?? record.SessionId;
        record.ThreadId = UsageTelemetryQuickReportSupport.NormalizeOptional(threadId)
                          ?? UsageTelemetryQuickReportSupport.NormalizeOptional(sessionId)
                          ?? record.ThreadId;
        record.TurnId = UsageTelemetryQuickReportSupport.NormalizeOptional(turnId) ?? record.TurnId;
        record.ResponseId = UsageTelemetryQuickReportSupport.NormalizeOptional(responseId) ?? record.ResponseId;
        record.Model = UsageTelemetryQuickReportSupport.NormalizeOptional(model) ?? record.Model;
        record.Surface = UsageTelemetryQuickReportSupport.NormalizeOptional(surface) ?? record.Surface;
        record.DurationMs = durationMs ?? record.DurationMs;
        record.TruthLevel = truthLevel;
        record.RawHash = UsageTelemetryQuickReportSupport.NormalizeOptional(rawHash) ?? record.RawHash;
    }

    private static IReadOnlyList<UsageEventRecord> DeduplicateImportedRecords(IReadOnlyList<UsageEventRecord> records) {
        if (records.Count <= 1) {
            return records;
        }

        var store = new InMemoryUsageEventStore();
        store.UpsertRange(records);
        return store.GetAll();
    }
}
