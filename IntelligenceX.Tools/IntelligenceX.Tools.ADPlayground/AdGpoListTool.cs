using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists Group Policy Objects across a forest or a single domain (read-only, capped).
/// </summary>
public sealed class AdGpoListTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private sealed record GpoListRequest(
        string? DomainName,
        string? ForestName,
        string? NameContains,
        GpoConsistency? Consistency,
        GpoLinkState? LinkState,
        DateTime? ModifiedSinceUtc,
        int MaxResults);

    private static readonly IReadOnlyDictionary<string, GpoConsistency> ConsistencyByName =
        new Dictionary<string, GpoConsistency>(StringComparer.OrdinalIgnoreCase) {
            ["ad_and_sysvol"] = GpoConsistency.AdAndSysvol,
            ["ad_only"] = GpoConsistency.AdOnly,
            ["sysvol_only"] = GpoConsistency.SysvolOnly
        };

    private static readonly IReadOnlyDictionary<string, GpoLinkState> LinkStateByName =
        new Dictionary<string, GpoLinkState>(StringComparer.OrdinalIgnoreCase) {
            ["unlinked"] = GpoLinkState.Unlinked,
            ["linked_enabled"] = GpoLinkState.LinkedEnabled,
            ["linked_disabled"] = GpoLinkState.LinkedDisabled,
            ["linked_broken"] = GpoLinkState.LinkedBroken
        };

    private static readonly IReadOnlyDictionary<GpoConsistency, string> ConsistencyNames =
        new Dictionary<GpoConsistency, string> {
            [GpoConsistency.AdAndSysvol] = "ad_and_sysvol",
            [GpoConsistency.AdOnly] = "ad_only",
            [GpoConsistency.SysvolOnly] = "sysvol_only"
        };

    private static readonly IReadOnlyDictionary<GpoLinkState, string> LinkStateNames =
        new Dictionary<GpoLinkState, string> {
            [GpoLinkState.Unlinked] = "unlinked",
            [GpoLinkState.LinkedEnabled] = "linked_enabled",
            [GpoLinkState.LinkedDisabled] = "linked_disabled",
            [GpoLinkState.LinkedBroken] = "linked_broken"
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_list",
        "List Group Policy Objects across a forest or domain with optional filters (read-only, capped).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name (defaults to current forest).")),
                ("domain_name", ToolSchema.String("Optional domain DNS name filter.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive substring filter for GPO display name.")),
                ("consistency", ToolSchema.String("Optional GPO AD/SYSVOL consistency filter.").Enum("any", "ad_and_sysvol", "ad_only", "sysvol_only")),
                ("link_state", ToolSchema.String("Optional link-state filter.").Enum("any", "unlinked", "linked_enabled", "linked_disabled", "linked_broken")),
                ("modified_since_utc", ToolSchema.String("Optional ISO-8601 UTC lower bound for last modification timestamp.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdGpoListResult(
        string? ForestName,
        string? DomainName,
        int Scanned,
        bool Truncated,
        IReadOnlyList<GpoListItem> Items);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoListTool"/> class.
    /// </summary>
    public AdGpoListTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<GpoListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("consistency"),
                    ConsistencyByName,
                    "consistency",
                    out GpoConsistency? consistency,
                    out var consistencyError)) {
                return ToolRequestBindingResult<GpoListRequest>.Failure(consistencyError ?? "Invalid consistency value.");
            }

            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("link_state"),
                    LinkStateByName,
                    "link_state",
                    out GpoLinkState? linkState,
                    out var linkStateError)) {
                return ToolRequestBindingResult<GpoListRequest>.Failure(linkStateError ?? "Invalid link_state value.");
            }

            if (!ToolTime.TryParseUtcOptional(reader.OptionalString("modified_since_utc"), out var modifiedSinceUtc, out var modifiedSinceError)) {
                return ToolRequestBindingResult<GpoListRequest>.Failure($"modified_since_utc: {modifiedSinceError}");
            }

            return ToolRequestBindingResult<GpoListRequest>.Success(new GpoListRequest(
                DomainName: reader.OptionalString("domain_name"),
                ForestName: reader.OptionalString("forest_name"),
                NameContains: reader.OptionalString("name_contains"),
                Consistency: consistency,
                LinkState: linkState,
                ModifiedSinceUtc: modifiedSinceUtc,
                MaxResults: reader.CappedInt32("max_results", Options.MaxResults, 1, Options.MaxResults)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var domainName = request.DomainName;
        var forestName = request.ForestName;
        var maxResults = request.MaxResults;
        var nameContains = request.NameContains;
        var consistency = request.Consistency;
        var linkState = request.LinkState;
        var modifiedSinceUtc = request.ModifiedSinceUtc;
        var items = new List<GpoListItem>(Math.Min(maxResults, 512));
        var scanned = 0;
        var truncated = false;

        try {
            foreach (var item in GpoListService.GetList(forestName, domainName)) {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                if (!Matches(item, nameContains, modifiedSinceUtc, consistency, linkState)) {
                    continue;
                }

                if (items.Count >= maxResults) {
                    truncated = true;
                    break;
                }

                items.Add(item);
            }
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "GPO list query failed.",
                invalidOperationErrorCode: "query_failed"));
        }

        var result = new AdGpoListResult(
            ForestName: forestName,
            DomainName: domainName,
            Scanned: scanned,
            Truncated: truncated,
            Items: items);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: items,
            viewRowsPath: "items_view",
            title: "Active Directory: GPO list (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
                if (!string.IsNullOrWhiteSpace(nameContains)) {
                    meta.Add("name_contains", nameContains);
                }
                if (consistency.HasValue) {
                    meta.Add("consistency", ToolEnumBinders.ToName(consistency.Value, ConsistencyNames));
                }
                if (linkState.HasValue) {
                    meta.Add("link_state", ToolEnumBinders.ToName(linkState.Value, LinkStateNames));
                }
                if (modifiedSinceUtc.HasValue) {
                    meta.Add("modified_since_utc", ToolTime.FormatUtc(modifiedSinceUtc));
                }
            }));
    }

    private static bool Matches(
        GpoListItem item,
        string? nameContains,
        DateTime? modifiedSinceUtc,
        GpoConsistency? consistency,
        GpoLinkState? linkState) {
        if (!string.IsNullOrWhiteSpace(nameContains) &&
            item.DisplayName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        if (modifiedSinceUtc.HasValue && item.Modified.ToUniversalTime() < modifiedSinceUtc.Value) {
            return false;
        }

        if (consistency.HasValue && item.Consistency != consistency.Value) {
            return false;
        }

        if (linkState.HasValue && item.LinkState != linkState.Value) {
            return false;
        }

        return true;
    }
}
