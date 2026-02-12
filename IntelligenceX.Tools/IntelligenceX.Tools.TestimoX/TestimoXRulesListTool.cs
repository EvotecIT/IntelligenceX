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
        "List TestimoX rules with metadata (scope, categories, tags, cost, visibility).",
        ToolSchema.Object(
                ("include_disabled", ToolSchema.Boolean("Include rules marked disabled. Default false.")),
                ("include_hidden", ToolSchema.Boolean("Include hidden rules. Default false.")),
                ("include_deprecated", ToolSchema.Boolean("Include deprecated rules. Default true.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across rule name/display/description.")),
                ("categories", ToolSchema.Array(ToolSchema.String(), "Optional category-name filters (any-match).")),
                ("tags", ToolSchema.Array(ToolSchema.String(), "Optional tag filters (any-match).")),
                ("max_rules", ToolSchema.Integer("Maximum rules returned (capped).")))
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
        var requestedCategories = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("categories"));
        var requestedTags = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("tags"));
        var maxRules = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "max_rules",
            defaultValue: Options.MaxRulesInCatalog,
            minInclusive: 1,
            maxInclusive: Options.MaxRulesInCatalog);

        List<Rule> discovered;
        try {
            var runner = new TestimoRunner();
            discovered = await runner.DiscoverRulesAsync(
                includeDisabled: true,
                ct: cancellationToken).ConfigureAwait(false);
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
                ContainsIgnoreCase(rule.Name, term) ||
                ContainsIgnoreCase(rule.DisplayName, term) ||
                ContainsIgnoreCase(rule.Description, term));
        }

        if (requestedCategories.Count > 0) {
            var requested = new HashSet<string>(requestedCategories, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Category.Any(cat => requested.Contains(cat.ToString())));
        }

        if (requestedTags.Count > 0) {
            var requested = new HashSet<string>(requestedTags, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Tags.Any(tag => requested.Contains(tag)));
        }

        var rows = filtered
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxRules + 1)
            .Select(static rule => new TestimoRuleCatalogRow(
                RuleName: rule.Name,
                DisplayName: rule.DisplayName,
                Description: rule.Description,
                Enabled: rule.Enable,
                Visibility: rule.Visibility.ToString(),
                IsDeprecated: rule.IsDeprecated,
                Scope: rule.Scope.ToString(),
                PermissionRequired: rule.PermissionRequired.ToString(),
                Cost: rule.Cost.ToString(),
                Categories: rule.Category.Select(static x => x.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Tags: rule.Tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToList();

        var truncated = rows.Count > maxRules;
        if (truncated) {
            rows = rows.Take(maxRules).ToList();
        }

        var model = new TestimoRulesCatalogResult(
            DiscoveredCount: discovered.Count,
            MatchedCount: rows.Count,
            Truncated: truncated,
            Rules: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rules_view",
            title: "TestimoX rules (preview)",
            maxTop: Options.MaxRulesInCatalog,
            baseTruncated: truncated,
            response: out var response,
            scanned: discovered.Count);
        return response;
    }

    private static bool ContainsIgnoreCase(string? value, string term) {
        return !string.IsNullOrWhiteSpace(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed record TestimoRulesCatalogResult(
        int DiscoveredCount,
        int MatchedCount,
        bool Truncated,
        IReadOnlyList<TestimoRuleCatalogRow> Rules);

    private sealed record TestimoRuleCatalogRow(
        string RuleName,
        string DisplayName,
        string Description,
        bool Enabled,
        string Visibility,
        bool IsDeprecated,
        string Scope,
        string PermissionRequired,
        string Cost,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags);
}
