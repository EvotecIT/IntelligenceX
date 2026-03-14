using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage.Claude;

/// <summary>
/// Imports exact token usage from local Claude session JSONL files.
/// </summary>
public sealed class ClaudeSessionUsageAdapter : IUsageTelemetryAdapter {
    /// <summary>
    /// Stable adapter id for Claude session logs.
    /// </summary>
    public const string StableAdapterId = "claude.session-log";

    private const string ParserVersion = "claude.session-log/v1";

    /// <inheritdoc />
    public string AdapterId => StableAdapterId;

    /// <inheritdoc />
    public bool CanImport(SourceRootRecord root) {
        if (root is null || !root.Enabled) {
            return false;
        }

        return ClaudeSessionImportSupport.IsClaudeProvider(root.ProviderId) &&
               (File.Exists(root.Path) || Directory.Exists(root.Path));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UsageEventRecord>> ImportAsync(
        SourceRootRecord root,
        UsageImportContext context,
        CancellationToken cancellationToken = default(CancellationToken)) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }
        if (!CanImport(root)) {
            throw new InvalidOperationException($"Root '{root.Path}' is not a supported Claude session source.");
        }

        var records = UsageTelemetryImportSupport.ImportArtifacts(
            root,
            context,
            StableAdapterId,
            ParserVersion,
            ClaudeSessionImportSupport.EnumerateCandidateFiles(root.Path, context.PreferRecentArtifacts),
            (filePath, artifact) => ClaudeSessionImport.ParseFile(filePath, root, StableAdapterId, context.MachineId, cancellationToken),
            cancellationToken);

        return Task.FromResult<IReadOnlyList<UsageEventRecord>>(records
            .OrderBy(record => record.TimestampUtc)
            .ThenBy(record => record.EventId, StringComparer.Ordinal)
            .ToArray());
    }
}
