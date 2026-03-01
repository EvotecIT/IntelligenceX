using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Definitions;
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
            "rules",
            "fallback_hint_keys:search_text,rule_origin,categories,tags,source_types"
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
            var pageSize = ResolvePageSize(arguments);
            if (!TryReadOffset(arguments, out var offset, out var offsetError)) {
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
        var searchText = context.Request.SearchText;
        var sourceTypeFilter = context.Request.SourceTypeFilter;
        var ruleOrigin = context.Request.RuleOrigin;
        var requestedCategories = context.Request.RequestedCategories;
        var requestedTags = context.Request.RequestedTags;
        var powerShellRulesDirectory = context.Request.PowerShellRulesDirectory;
        var pageSize = context.Request.PageSize;
        var offset = context.Request.Offset;

        List<Rule> discovered;
        var usingExternalDirectory = !string.IsNullOrWhiteSpace(powerShellRulesDirectory);
        HashSet<string>? builtinRuleNames = null;
        var runner = new TestimoRunner();
        var discovery = await TryDiscoverRulesAsync(
            runner,
            powerShellRulesDirectory,
            cancellationToken,
            defaultErrorMessage: "TestimoX rule discovery failed.").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(discovery.ErrorResponse)) {
            return discovery.ErrorResponse!;
        }

        discovered = discovery.Rules ?? new List<Rule>();
        if (usingExternalDirectory) {
            var builtinDiscovery = await TryDiscoverBuiltinRuleNamesAsync(
                cancellationToken,
                defaultErrorMessage: "TestimoX builtin rule discovery failed.").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(builtinDiscovery.ErrorResponse)) {
                return builtinDiscovery.ErrorResponse!;
            }

            builtinRuleNames = builtinDiscovery.RuleNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        IEnumerable<Rule> filtered = TestimoXRuleSelectionHelper.ApplyVisibilityFilters(
            discovered,
            includeDisabled: includeDisabled,
            includeHidden: includeHidden,
            includeDeprecated: includeDeprecated);
        filtered = TestimoXRuleSelectionHelper.ApplySharedFilters(
            filtered,
            searchText,
            requestedCategories,
            requestedTags,
            sourceTypeFilter,
            ruleOrigin,
            usingExternalDirectory,
            builtinRuleNames);

        var matchedRows = filtered
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(rule => new TestimoRuleCatalogRow(
                RuleName: rule.Name,
                DisplayName: rule.DisplayName,
                Description: rule.Description,
                SourceType: TestimoXRuleSelectionHelper.GetSourceType(rule),
                Enabled: rule.Enable,
                Visibility: rule.Visibility.ToString(),
                IsDeprecated: rule.IsDeprecated,
                Scope: rule.Scope.ToString(),
                PermissionRequired: rule.PermissionRequired.ToString(),
                Cost: rule.Cost.ToString(),
                RuleOrigin: TestimoXRuleSelectionHelper.ResolveRuleOrigin(rule, usingExternalDirectory, builtinRuleNames),
                Categories: rule.Category.Select(static x => x.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Tags: rule.Tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray()))
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
            DiscoveredCount: discovered.Count,
            MatchedCount: matchedRows.Count,
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
            maxTop: Math.Max(Options.MaxRulesInCatalog, matchedRows.Count),
            baseTruncated: truncated,
            scanned: discovered.Count,
            metaMutate: meta => {
                meta.Add("matched_count", matchedRows.Count);
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

    private int? ResolvePageSize(JsonObject? arguments) {
        if (!HasArgument(arguments, "page_size") && !HasArgument(arguments, "max_rules")) {
            return null;
        }

        var pageSize = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "page_size",
            defaultValue: Options.MaxRulesInCatalog,
            minInclusive: 1,
            maxInclusive: Options.MaxRulesInCatalog);

        // Backward-compatible alias for older callers.
        if (!HasArgument(arguments, "page_size") && HasArgument(arguments, "max_rules")) {
            pageSize = ToolArgs.GetCappedInt32(
                arguments: arguments,
                key: "max_rules",
                defaultValue: pageSize,
                minInclusive: 1,
                maxInclusive: Options.MaxRulesInCatalog);
        }

        return pageSize;
    }

    private static bool TryReadOffset(JsonObject? arguments, out int offset, out string? error) {
        offset = 0;
        error = null;

        var rawCursor = ToolArgs.GetOptionalTrimmed(arguments, "cursor");
        var cursorOffset = 0;
        if (!string.IsNullOrWhiteSpace(rawCursor)) {
            if (!OffsetCursor.TryDecode(rawCursor, out var decoded) || decoded < 0 || decoded > int.MaxValue) {
                error = "cursor is invalid. Use cursor returned by previous page response.";
                return false;
            }

            cursorOffset = (int)decoded;
        }

        if (!TryGetInt64Argument(arguments, "offset", out var rawOffset)) {
            offset = cursorOffset;
            return true;
        }

        if (rawOffset < 0 || rawOffset > int.MaxValue) {
            error = "offset must be between 0 and 2147483647.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rawCursor) && rawOffset != cursorOffset) {
            error = "Provide either cursor or offset (or keep them aligned), not conflicting values.";
            return false;
        }

        offset = (int)rawOffset;
        return true;
    }

    private static bool TryGetInt64Argument(JsonObject? arguments, string name, out long value) {
        value = 0;
        if (arguments is null || string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (!string.Equals((kv.Key ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var parsed = kv.Value.AsInt64();
            if (!parsed.HasValue) {
                return false;
            }

            value = parsed.Value;
            return true;
        }

        return false;
    }

    private static bool HasArgument(JsonObject? arguments, string name) {
        if (arguments is null || string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (string.Equals((kv.Key ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
