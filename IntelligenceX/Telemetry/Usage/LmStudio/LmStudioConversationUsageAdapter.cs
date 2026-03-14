using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage.LmStudio;

/// <summary>
/// Imports exact token usage from LM Studio conversation JSON files.
/// </summary>
public sealed class LmStudioConversationUsageAdapter : IUsageTelemetryAdapter {
    /// <summary>
    /// Stable adapter id for LM Studio conversation files.
    /// </summary>
    public const string StableAdapterId = "lmstudio.conversation";

    private const string ParserVersion = "lmstudio.conversation/v1";

    /// <inheritdoc />
    public string AdapterId => StableAdapterId;

    /// <inheritdoc />
    public bool CanImport(SourceRootRecord root) {
        if (root is null || !root.Enabled) {
            return false;
        }

        return LmStudioConversationImport.IsLmStudioProvider(root.ProviderId) &&
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
            throw new InvalidOperationException($"Root '{root.Path}' is not a supported LM Studio conversation source.");
        }

        var records = UsageTelemetryImportSupport.ImportArtifacts(
            root,
            context,
            StableAdapterId,
            ParserVersion,
            LmStudioConversationImport.EnumerateCandidateFiles(root.Path, context.PreferRecentArtifacts),
            (filePath, artifact) => LmStudioConversationImport.ParseFile(
                    filePath,
                    root,
                    StableAdapterId,
                    context.MachineId,
                    cancellationToken),
            cancellationToken);

        return Task.FromResult<IReadOnlyList<UsageEventRecord>>(records);
    }
}
