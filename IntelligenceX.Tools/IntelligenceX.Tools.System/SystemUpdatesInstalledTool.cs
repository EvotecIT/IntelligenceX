using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Updates;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists installed updates (and optional pending local updates) from ComputerX (read-only, capped).
/// </summary>
public sealed class SystemUpdatesInstalledTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_updates_installed",
        "List installed updates (and optional pending local updates) for a computer (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_pending_local", ToolSchema.Boolean("When true and querying local machine, include pending updates from WUA COM.")),
                ("title_contains", ToolSchema.String("Optional case-insensitive filter against update title.")),
                ("kb_contains", ToolSchema.String("Optional case-insensitive filter against KB identifier.")),
                ("installed_after_utc", ToolSchema.String("Optional UTC timestamp filter (RFC3339/ISO-8601).")),
                ("max_results", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record SystemUpdatesInstalledResult(
        string ComputerName,
        bool IncludePendingLocal,
        bool PendingIncluded,
        string? TitleContains,
        string? KbContains,
        string? InstalledAfterUtc,
        int Scanned,
        bool Truncated,
        IReadOnlyList<UpdateInfo> Updates);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemUpdatesInstalledTool"/> class.
    /// </summary>
    public SystemUpdatesInstalledTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_updates_installed");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = ResolveTargetComputerName(computerName);
        var includePendingLocal = ToolArgs.GetBoolean(arguments, "include_pending_local", defaultValue: false);
        var titleContains = ToolArgs.GetOptionalTrimmed(arguments, "title_contains");
        var kbContains = ToolArgs.GetOptionalTrimmed(arguments, "kb_contains");
        var installedAfterRaw = ToolArgs.GetOptionalTrimmed(arguments, "installed_after_utc");
        var maxResults = ResolveMaxResults(arguments);

        DateTime? installedAfterUtc = null;
        if (!string.IsNullOrWhiteSpace(installedAfterRaw)) {
            if (!DateTime.TryParse(installedAfterRaw, out var parsed)) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "installed_after_utc must be a valid ISO-8601 datetime."));
            }
            installedAfterUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        if (!TryGetInstalledAndPendingUpdates(
                computerName: computerName,
                target: target,
                includePendingLocal: includePendingLocal,
                updates: out var rows,
                pendingIncluded: out var pendingIncluded,
                errorResponse: out var updateError)) {
            return Task.FromResult(updateError!);
        }

        var filtered = rows
            .Where(x => string.IsNullOrWhiteSpace(titleContains)
                || x.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(kbContains)
                || SystemPatchKbNormalization.MatchesContainsFilter(
                    SystemPatchKbNormalization.NormalizeDistinct(new[] { x.Kb, x.Title }),
                    kbContains))
            .Where(x => !installedAfterUtc.HasValue
                || (x.InstalledOn.HasValue && x.InstalledOn.Value.ToUniversalTime() >= installedAfterUtc.Value))
            .ToArray();

        var viewRows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new SystemUpdatesInstalledResult(
            ComputerName: target,
            IncludePendingLocal: includePendingLocal,
            PendingIncluded: pendingIncluded,
            TitleContains: titleContains,
            KbContains: kbContains,
            InstalledAfterUtc: installedAfterUtc?.ToString("O"),
            Scanned: scanned,
            Truncated: truncated,
            Updates: viewRows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: viewRows,
            viewRowsPath: "updates_view",
            title: "System updates (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, target);
                AddPendingLocalMeta(meta, includePendingLocal, pendingIncluded);
                AddMaxResultsMeta(meta, maxResults);
                if (!string.IsNullOrWhiteSpace(titleContains)) {
                    meta.Add("title_contains", titleContains);
                }
                if (!string.IsNullOrWhiteSpace(kbContains)) {
                    meta.Add("kb_contains", kbContains);
                }
                if (installedAfterUtc.HasValue) {
                    meta.Add("installed_after_utc", installedAfterUtc.Value.ToString("O"));
                }
            });
        return Task.FromResult(response);
    }
}
