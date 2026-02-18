using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns focused GPO inventory health slices (disabled/empty/unlinked/all-settings-disabled) for one domain (read-only).
/// </summary>
public sealed class AdGpoInventoryHealthTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_inventory_health",
        "Assess GPO inventory health slices for one domain (disabled, empty, unlinked, all-settings-disabled; read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("slice", ToolSchema.String("Optional slice selector: all, disabled, empty, unlinked, all_settings_disabled. Default all.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoInventoryHealthResult(
        string DomainName,
        string Slice,
        int GposEnumerated,
        int Scanned,
        bool Truncated,
        int DisabledCount,
        int EmptyCount,
        int UnlinkedCount,
        int AllSettingsDisabledCount,
        IReadOnlyList<GpoInventoryHealthService.GpoHealthItem> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoInventoryHealthTool"/> class.
    /// </summary>
    public AdGpoInventoryHealthTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var slice = (ToolArgs.GetOptionalTrimmed(arguments, "slice") ?? "all").ToLowerInvariant();
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var view = GpoInventoryHealthService.Get(domainName);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "GPO inventory health query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        IReadOnlyList<GpoInventoryHealthService.GpoHealthItem> selectedRows = slice switch {
            "all" => view.All,
            "disabled" => view.Disabled,
            "empty" => view.Empty,
            "unlinked" => view.Unlinked,
            "all_settings_disabled" => view.AllSettingsDisabled,
            _ => Array.Empty<GpoInventoryHealthService.GpoHealthItem>()
        };

        if (slice is not ("all" or "disabled" or "empty" or "unlinked" or "all_settings_disabled")) {
            return Task.FromResult(ToolResponse.Error(
                "invalid_argument",
                "slice must be one of: all, disabled, empty, unlinked, all_settings_disabled."));
        }

        var scanned = selectedRows.Count;
        var rows = scanned > maxResults ? selectedRows.Take(maxResults).ToArray() : selectedRows;
        var truncated = scanned > rows.Count;

        var result = new AdGpoInventoryHealthResult(
            DomainName: domainName,
            Slice: slice,
            GposEnumerated: view.GposEnumerated,
            Scanned: scanned,
            Truncated: truncated,
            DisabledCount: view.Disabled.Count,
            EmptyCount: view.Empty.Count,
            UnlinkedCount: view.Unlinked.Count,
            AllSettingsDisabledCount: view.AllSettingsDisabled.Count,
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Inventory Health (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("slice", slice);
                meta.Add("gpos_enumerated", view.GposEnumerated);
                meta.Add("max_results", maxResults);
            }));
    }
}
