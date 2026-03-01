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
    private sealed record UpdatesInstalledRequest(
        string? ComputerName,
        string Target,
        bool IncludePendingLocal,
        string? TitleContains,
        string? KbContains,
        DateTime? InstalledAfterUtc,
        int MaxResults);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<UpdatesInstalledRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var installedAfterRaw = reader.OptionalString("installed_after_utc");

            DateTime? installedAfterUtc = null;
            if (!string.IsNullOrWhiteSpace(installedAfterRaw)) {
                if (!DateTime.TryParse(installedAfterRaw, out var parsed)) {
                    return ToolRequestBindingResult<UpdatesInstalledRequest>.Failure("installed_after_utc must be a valid ISO-8601 datetime.");
                }

                installedAfterUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            }

            return ToolRequestBindingResult<UpdatesInstalledRequest>.Success(new UpdatesInstalledRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludePendingLocal: reader.Boolean("include_pending_local", defaultValue: false),
                TitleContains: reader.OptionalString("title_contains"),
                KbContains: reader.OptionalString("kb_contains"),
                InstalledAfterUtc: installedAfterUtc,
                MaxResults: ResolveMaxResults(arguments)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<UpdatesInstalledRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var windowsError = ValidateWindowsSupport("system_updates_installed");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }
        if (!TryGetInstalledAndPendingUpdates(
                computerName: request.ComputerName,
                target: request.Target,
                includePendingLocal: request.IncludePendingLocal,
                updates: out var rows,
                pendingIncluded: out var pendingIncluded,
                errorResponse: out var updateError)) {
            return Task.FromResult(updateError!);
        }

        var filtered = rows
            .Where(x => string.IsNullOrWhiteSpace(request.TitleContains)
                || x.Title.Contains(request.TitleContains, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(request.KbContains)
                || SystemPatchKbNormalization.MatchesContainsFilter(
                    SystemPatchKbNormalization.NormalizeDistinct(new[] { x.Kb, x.Title }),
                    request.KbContains))
            .Where(x => !request.InstalledAfterUtc.HasValue
                || (x.InstalledOn.HasValue && x.InstalledOn.Value.ToUniversalTime() >= request.InstalledAfterUtc.Value))
            .ToArray();

        var viewRows = CapRows(filtered, request.MaxResults, out var scanned, out var truncated);

        var result = new SystemUpdatesInstalledResult(
            ComputerName: request.Target,
            IncludePendingLocal: request.IncludePendingLocal,
            PendingIncluded: pendingIncluded,
            TitleContains: request.TitleContains,
            KbContains: request.KbContains,
            InstalledAfterUtc: request.InstalledAfterUtc?.ToString("O"),
            Scanned: scanned,
            Truncated: truncated,
            Updates: viewRows);

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: viewRows,
            viewRowsPath: "updates_view",
            title: "System updates (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, request.Target);
                AddPendingLocalMeta(meta, request.IncludePendingLocal, pendingIncluded);
                AddMaxResultsMeta(meta, request.MaxResults);
                if (!string.IsNullOrWhiteSpace(request.TitleContains)) {
                    meta.Add("title_contains", request.TitleContains);
                }
                if (!string.IsNullOrWhiteSpace(request.KbContains)) {
                    meta.Add("kb_contains", request.KbContains);
                }
                if (request.InstalledAfterUtc.HasValue) {
                    meta.Add("installed_after_utc", request.InstalledAfterUtc.Value.ToString("O"));
                }
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_updates_installed",
                    targetComputer: request.Target,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: scanned,
                    truncated: truncated);
            });
        return Task.FromResult(response);
    }
}
