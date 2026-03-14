using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage.Copilot;

/// <summary>
/// Imports local GitHub Copilot CLI session activity from session-state logs.
/// </summary>
public sealed class CopilotSessionUsageAdapter : IUsageTelemetryAdapter {
    /// <summary>
    /// Stable adapter id for Copilot CLI session-state logs.
    /// </summary>
    public const string StableAdapterId = "copilot.session-state";

    private const string ParserVersion = "copilot.session-state/v1";

    /// <inheritdoc />
    public string AdapterId => StableAdapterId;

    /// <inheritdoc />
    public bool CanImport(SourceRootRecord root) {
        if (root is null || !root.Enabled) {
            return false;
        }

        return CopilotSessionImportSupport.IsCopilotProvider(root.ProviderId) &&
               (File.Exists(root.Path) || Directory.Exists(root.Path));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UsageEventRecord>> ImportAsync(
        SourceRootRecord root,
        UsageImportContext context,
        CancellationToken cancellationToken = default) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }
        if (!CanImport(root)) {
            throw new InvalidOperationException($"Root '{root.Path}' is not a supported Copilot session source.");
        }

        var records = UsageTelemetryImportSupport.ImportArtifacts(
            root,
            context,
            StableAdapterId,
            ParserVersion,
            CopilotSessionImportSupport.EnumerateCandidateFiles(root.Path, context.PreferRecentArtifacts),
            (filePath, artifact) => CopilotSessionImport.ParseFile(filePath, root, StableAdapterId, context.MachineId, cancellationToken),
            cancellationToken);

        return Task.FromResult(records);
    }
}
