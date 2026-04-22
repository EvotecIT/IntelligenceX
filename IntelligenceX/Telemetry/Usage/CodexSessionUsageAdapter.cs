using System;
using IntelligenceX.Telemetry.Usage.Codex;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Imports exact token usage from local Codex session rollout files.
/// </summary>
public sealed class CodexSessionUsageAdapter : IUsageTelemetryAdapter {
    /// <summary>
    /// Stable adapter id for Codex rollout/session files.
    /// </summary>
    public const string StableAdapterId = "codex.session-log";

    private const string ParserVersion = "codex.session-log/v4";

    /// <inheritdoc />
    public string AdapterId => StableAdapterId;

    /// <inheritdoc />
    public bool CanImport(SourceRootRecord root) {
        if (root is null || !root.Enabled) {
            return false;
        }

        return CodexSessionImportSupport.IsCodexProvider(root.ProviderId) &&
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
            throw new InvalidOperationException($"Root '{root.Path}' is not a supported Codex session source.");
        }

        var records = UsageTelemetryImportSupport.ImportArtifacts(
            root,
            context,
            StableAdapterId,
            ParserVersion,
            CodexSessionImportSupport.EnumerateCandidateFiles(root.Path, context.PreferRecentArtifacts),
            (filePath, artifact) => CodexSessionImport.ParseFile(filePath, root, StableAdapterId, context.MachineId, cancellationToken),
            cancellationToken);

        return Task.FromResult(records);
    }
}
