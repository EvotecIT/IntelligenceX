using System;
using System.Collections.Generic;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Codex;

internal static class CodexQuickReportImport {
    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        foreach (var file in CodexSessionImportSupport.EnumerateCandidateFiles(rootPath, preferRecentArtifacts)) {
            yield return file;
        }
    }

    public static IReadOnlyList<UsageEventRecord> ParseFile(
        SourceRootRecord root,
        string filePath,
        UsageTelemetryQuickReportOptions options,
        string adapterId,
        string providerId,
        CancellationToken cancellationToken) {
        return CodexSessionImport.ParseFile(
            filePath,
            root,
            adapterId,
            options.MachineId,
            cancellationToken);
    }
}
