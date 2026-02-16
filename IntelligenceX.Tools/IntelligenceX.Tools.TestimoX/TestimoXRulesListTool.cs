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
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRulesListTool"/> class.
    /// </summary>
    public TestimoXRulesListTool(TestimoXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResponse.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_rules_list." },
                isTransient: false);
        }

        var includeDisabled = ToolArgs.GetBoolean(arguments, "include_disabled", defaultValue: false);
        var includeHidden = ToolArgs.GetBoolean(arguments, "include_hidden", defaultValue: false);
        var includeDeprecated = ToolArgs.GetBoolean(arguments, "include_deprecated", defaultValue: true);
        var searchText = ToolArgs.GetOptionalTrimmed(arguments, "search_text");
        var requestedSourceTypes = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("source_types"));
        if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(requestedSourceTypes, out var sourceTypeFilter, out var sourceTypeError)) {
            return ToolResponse.Error("invalid_argument", sourceTypeError ?? "Invalid source_types argument.");
        }
        if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                ToolArgs.GetOptionalTrimmed(arguments, "rule_origin"),
                out var ruleOrigin,
                out var ruleOriginError)) {
            return ToolResponse.Error("invalid_argument", ruleOriginError ?? "Invalid rule_origin argument.");
        }
        var requestedCategories = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("categories"));
        var requestedTags = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("tags"));
        var powerShellRulesDirectory = ToolArgs.GetOptionalTrimmed(arguments, "powershell_rules_directory");
        var pageSize = ResolvePageSize(arguments);
        if (!TryReadOffset(arguments, out var offset, out var offsetError)) {
            return ToolResponse.Error("invalid_argument", offsetError ?? "Invalid offset argument.");
        }

        List<Rule> discovered;
        var usingExternalDirectory = !string.IsNullOrWhiteSpace(powerShellRulesDirectory);
        HashSet<string>? builtinRuleNames = null;
        try {
            var runner = new TestimoRunner();
            discovered = await runner.DiscoverRulesAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: powerShellRulesDirectory).ConfigureAwait(false);

            if (usingExternalDirectory) {
                builtinRuleNames = await TestimoXRuleSelectionHelper.DiscoverBuiltinRuleNamesAsync(cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error("query_failed", $"TestimoX rule discovery failed: {ex.Message}");
        }

        IEnumerable<Rule> filtered = discovered;
        if (!includeDisabled) {
            filtered = filtered.Where(static x => x.Enable);
        }
        if (!includeHidden) {
            filtered = filtered.Where(static x => x.Visibility != RuleVisibility.Hidden);
        }
        if (!includeDeprecated) {
            filtered = filtered.Where(static x => !x.IsDeprecated);
        }

        if (!string.IsNullOrWhiteSpace(searchText)) {
            var term = searchText.Trim();
            filtered = filtered.Where(rule =>
                TestimoXRuleSelectionHelper.ContainsIgnoreCase(rule.Name, term) ||
                TestimoXRuleSelectionHelper.ContainsIgnoreCase(rule.DisplayName, term) ||
                TestimoXRuleSelectionHelper.ContainsIgnoreCase(rule.Description, term));
        }

        if (sourceTypeFilter is { Count: > 0 }) {
            filtered = filtered.Where(rule => TestimoXRuleSelectionHelper.MatchesSourceType(rule, sourceTypeFilter));
        }

        if (requestedCategories.Count > 0) {
            var requested = new HashSet<string>(requestedCategories, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Category.Any(cat => requested.Contains(cat.ToString())));
        }

        if (requestedTags.Count > 0) {
            var requested = new HashSet<string>(requestedTags, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Tags.Any(tag => requested.Contains(tag)));
        }

        if (!string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase)) {
            filtered = filtered.Where(rule =>
                string.Equals(
                    TestimoXRuleSelectionHelper.ResolveRuleOrigin(rule, usingExternalDirectory, builtinRuleNames),
                    ruleOrigin,
                    StringComparison.OrdinalIgnoreCase));
        }

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

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rules_view",
            title: "TestimoX rules (preview)",
            maxTop: Math.Max(Options.MaxRulesInCatalog, matchedRows.Count),
            baseTruncated: truncated,
            response: out var response,
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

        var rawOffset = arguments?.GetInt64("offset");
        if (!rawOffset.HasValue) {
            offset = cursorOffset;
            return true;
        }

        if (rawOffset.Value < 0 || rawOffset.Value > int.MaxValue) {
            error = "offset must be between 0 and 2147483647.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rawCursor) && rawOffset.Value != cursorOffset) {
            error = "Provide either cursor or offset (or keep them aligned), not conflicting values.";
            return false;
        }

        offset = (int)rawOffset.Value;
        return true;
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
