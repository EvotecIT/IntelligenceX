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

        if (!OperatingSystem.IsWindows()) {
            return Task.FromResult(ToolResponse.Error("not_supported", "system_updates_installed is available only on Windows hosts."));
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName!;
        var includePendingLocal = ToolArgs.GetBoolean(arguments, "include_pending_local", defaultValue: false);
        var titleContains = ToolArgs.GetOptionalTrimmed(arguments, "title_contains");
        var kbContains = ToolArgs.GetOptionalTrimmed(arguments, "kb_contains");
        var installedAfterRaw = ToolArgs.GetOptionalTrimmed(arguments, "installed_after_utc");
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        DateTime? installedAfterUtc = null;
        if (!string.IsNullOrWhiteSpace(installedAfterRaw)) {
            if (!DateTime.TryParse(installedAfterRaw, out var parsed)) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "installed_after_utc must be a valid ISO-8601 datetime."));
            }
            installedAfterUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        var rows = new List<UpdateInfo>();
        try {
            rows.AddRange(Updates.GetInstalled(computerName));
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", $"Installed updates query failed: {ex.Message}"));
        }

        var isLocalTarget = string.IsNullOrWhiteSpace(computerName)
            || string.Equals(computerName, ".", StringComparison.Ordinal)
            || string.Equals(target, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        var pendingIncluded = false;
        if (includePendingLocal && isLocalTarget) {
            try {
                rows.AddRange(Updates.GetPending());
                pendingIncluded = true;
            } catch {
                pendingIncluded = false;
            }
        }

        var filtered = rows
            .Where(x => string.IsNullOrWhiteSpace(titleContains)
                || x.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(kbContains)
                || (x.Kb?.Contains(kbContains, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(x => !installedAfterUtc.HasValue
                || (x.InstalledOn.HasValue && x.InstalledOn.Value.ToUniversalTime() >= installedAfterUtc.Value))
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<UpdateInfo> viewRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > viewRows.Count;

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

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: viewRows,
            viewRowsPath: "updates_view",
            title: "System updates (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("computer_name", target);
                meta.Add("include_pending_local", includePendingLocal);
                meta.Add("pending_included", pendingIncluded);
                meta.Add("max_results", maxResults);
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

