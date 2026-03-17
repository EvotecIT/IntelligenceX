using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Lists TestimoX rules and exposes stable metadata for tool planning.
/// </summary>
public sealed class TestimoXRulesListTool : TestimoXToolBase, ITool {
    private sealed record RulesListRequest(
        bool IncludeDisabled,
        bool IncludeHidden,
        bool IncludeDeprecated,
        string? SearchText,
        HashSet<string>? SourceTypeFilter,
        string RuleOrigin,
        IReadOnlyList<string> RequestedCategories,
        IReadOnlyList<string> RequestedTags,
        string? PowerShellRulesDirectory,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_rules_list",
        "List TestimoX rules with metadata (scope, source_type, origin, categories, tags, cost, visibility).",
        ToolSchema.Object(
                ("include_disabled", ToolSchema.Boolean("Include rules marked disabled. Default false.")),
                ("include_hidden", ToolSchema.Boolean("Include hidden rules. Default false.")),
                ("include_deprecated", ToolSchema.Boolean("Include deprecated rules. Default true.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across rule name/display/description.")),
                ("source_types", ToolSchema.Array(ToolSchema.String("Rule source type.").Enum(TestimoXRuleSelectionHelper.SourceTypeNames), "Optional source type filters (any-match).")),
                ("rule_origin", ToolSchema.String("Optional origin filter. 'builtin' means bundled rules, 'external' means rules introduced through powershell_rules_directory.").Enum(TestimoXRuleSelectionHelper.RuleOriginNames)),
                ("categories", ToolSchema.Array(ToolSchema.String(), "Optional category-name filters (any-match).")),
                ("tags", ToolSchema.Array(ToolSchema.String(), "Optional tag filters (any-match).")),
                ("powershell_rules_directory", ToolSchema.String("Optional path to user PowerShell rule scripts.")),
                ("page_size", ToolSchema.Integer("Optional number of rules to return in this page.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched rules (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "rules"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRulesListTool"/> class.
    /// </summary>
    public TestimoXRulesListTool(TestimoXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<RulesListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var includeDisabled = reader.Boolean("include_disabled", defaultValue: false);
            var includeHidden = reader.Boolean("include_hidden", defaultValue: false);
            var includeDeprecated = reader.Boolean("include_deprecated", defaultValue: true);
            var searchText = reader.OptionalString("search_text");

            var requestedSourceTypes = reader.DistinctStringArray("source_types");
            if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(requestedSourceTypes, out var sourceTypeFilter, out var sourceTypeError)) {
                return ToolRequestBindingResult<RulesListRequest>.Failure(sourceTypeError ?? "Invalid source_types argument.");
            }

            if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                    reader.OptionalString("rule_origin"),
                    out var ruleOrigin,
                    out var ruleOriginError)) {
                return ToolRequestBindingResult<RulesListRequest>.Failure(ruleOriginError ?? "Invalid rule_origin argument.");
            }

            var requestedCategories = reader.DistinctStringArray("categories");
            var requestedTags = reader.DistinctStringArray("tags");
            var powerShellRulesDirectory = reader.OptionalString("powershell_rules_directory");
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<RulesListRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<RulesListRequest>.Success(new RulesListRequest(
                IncludeDisabled: includeDisabled,
                IncludeHidden: includeHidden,
                IncludeDeprecated: includeDeprecated,
                SearchText: searchText,
                SourceTypeFilter: sourceTypeFilter,
                RuleOrigin: ruleOrigin,
                RequestedCategories: requestedCategories,
                RequestedTags: requestedTags,
                PowerShellRulesDirectory: powerShellRulesDirectory,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<RulesListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_rules_list." },
                isTransient: false);
        }

        var includeDisabled = context.Request.IncludeDisabled;
        var includeHidden = context.Request.IncludeHidden;
        var includeDeprecated = context.Request.IncludeDeprecated;
        var pageSize = context.Request.PageSize;
        var offset = context.Request.Offset;
        ToolingRuleDiscoveryResult discovery;
        try {
            discovery = await ToolingRuleService.DiscoverRulesAsync(new ToolingRuleDiscoveryRequest {
                IncludeDisabled = includeDisabled,
                IncludeHidden = includeHidden,
                IncludeDeprecated = includeDeprecated,
                Query = context.Request.SearchText,
                Categories = context.Request.RequestedCategories,
                Tags = context.Request.RequestedTags,
                SourceTypes = ToToolingSourceTypes(context.Request.SourceTypeFilter),
                RuleOrigin = ToToolingRuleOrigin(context.Request.RuleOrigin),
                PowerShellRulesDirectory = context.Request.PowerShellRulesDirectory
            }, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "TestimoX rule discovery failed.");
        }

        var matchedRows = discovery.Rules
            .Select(row => new TestimoRuleCatalogRow(
                RuleName: row.Name,
                DisplayName: row.DisplayName,
                Description: row.Description,
                SourceType: NormalizeSourceTypeName(row.SourceType),
                Enabled: row.Enabled,
                Visibility: row.Visibility,
                IsDeprecated: row.IsDeprecated,
                Scope: row.Scope,
                PermissionRequired: row.PermissionRequired,
                Cost: row.Cost,
                RuleOrigin: row.RuleOrigin,
                Categories: row.Categories,
                Tags: row.Tags))
            .ToList();

        if (offset > matchedRows.Count) {
            offset = matchedRows.Count;
        }

        var pageRows = matchedRows.Skip(offset);
        var rows = pageSize.HasValue
            ? pageRows.Take(pageSize.Value).ToList()
            : pageRows.ToList();
        var truncatedByPage = pageSize.HasValue && offset + rows.Count < matchedRows.Count;
        var truncated = truncatedByPage;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new TestimoRulesCatalogResult(
            DiscoveredCount: discovery.DiscoveredCount,
            MatchedCount: discovery.MatchedCount,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: pageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPackCap: false,
            TruncatedByPage: truncatedByPage,
            Truncated: truncated,
            Rules: rows);

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rules_view",
            title: "TestimoX rules (preview)",
            maxTop: Math.Max(Options.MaxRulesInCatalog, discovery.MatchedCount),
            baseTruncated: truncated,
            scanned: discovery.DiscoveredCount,
            metaMutate: meta => {
                meta.Add("matched_count", discovery.MatchedCount);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                if (pageSize.HasValue) {
                    meta.Add("page_size", pageSize.Value);
                }
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                meta.Add("truncated_by_pack_cap", false);
                meta.Add("truncated_by_page", truncatedByPage);
            });
        return response;
    }

    private sealed record TestimoRulesCatalogResult(
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int? PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPackCap,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<TestimoRuleCatalogRow> Rules);

    private sealed record TestimoRuleCatalogRow(
        string RuleName,
        string DisplayName,
        string Description,
        string SourceType,
        bool Enabled,
        string Visibility,
        bool IsDeprecated,
        string Scope,
        string PermissionRequired,
        string Cost,
        string RuleOrigin,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags);
}
