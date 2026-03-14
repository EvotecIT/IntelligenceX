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
/// Returns focused rule provenance and source metadata for TestimoX rules.
/// </summary>
public sealed class TestimoXSourceQueryTool : TestimoXToolBase, ITool {
    private const int DefaultPageSize = 25;

    private sealed record SourceQueryRequest(
        IReadOnlyList<string> RequestedRuleNames,
        IReadOnlyList<string> RuleNamePatterns,
        bool IncludeDisabled,
        bool IncludeHidden,
        bool IncludeDeprecated,
        string? SearchText,
        HashSet<string>? SourceTypeFilter,
        string RuleOrigin,
        HashSet<string>? MigrationStateFilter,
        IReadOnlyList<string> RequestedCategories,
        IReadOnlyList<string> RequestedTags,
        RuleSelectionProfile? Profile,
        string? PowerShellRulesDirectory,
        bool IncludeResources,
        bool IncludeLinks,
        int PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_source_query",
        "Query focused TestimoX rule provenance including enum mapping, migration state, modules, supported systems, resources, and supersedence links.",
        ToolSchema.Object(
                ("rule_names", ToolSchema.Array(ToolSchema.String(), "Optional explicit rule names to include.")),
                ("rule_name_patterns", ToolSchema.Array(ToolSchema.String("Wildcard pattern matched against rule_name/display_name (for example: *ldap*)."), "Optional wildcard selectors.")),
                ("include_disabled", ToolSchema.Boolean("Include rules marked disabled. Default true.")),
                ("include_hidden", ToolSchema.Boolean("Include hidden rules. Default true.")),
                ("include_deprecated", ToolSchema.Boolean("Include deprecated rules. Default true.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across rule name/display/description/summary.")),
                ("source_types", ToolSchema.Array(ToolSchema.String("Rule source type.").Enum(TestimoXRuleSelectionHelper.SourceTypeNames), "Optional source type filters (any-match).")),
                ("rule_origin", ToolSchema.String("Optional origin filter. 'builtin' means bundled rules, 'external' means rules introduced through powershell_rules_directory.").Enum(TestimoXRuleSelectionHelper.RuleOriginNames)),
                ("migration_states", ToolSchema.Array(ToolSchema.String("Inventory migration state.").Enum(TestimoXRuleInventoryHelper.MigrationStateNames), "Optional migration-state filters (any-match).")),
                ("categories", ToolSchema.Array(ToolSchema.String(), "Optional category-name filters (any-match).")),
                ("tags", ToolSchema.Array(ToolSchema.String(), "Optional tag filters (any-match).")),
                ("profile", ToolSchema.String("Optional curated profile used to classify assessment areas.").Enum(GetProfileNames())),
                ("powershell_rules_directory", ToolSchema.String("Optional path to user PowerShell rule scripts.")),
                ("include_resources", ToolSchema.Boolean("Include guidance/reference resources. Default true.")),
                ("include_links", ToolSchema.Boolean("Include inter-rule relationship links. Default true.")),
                ("page_size", ToolSchema.Integer("Optional number of source rows to return in this page. Default 25.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched source rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "rules",
            "provenance"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXSourceQueryTool"/> class.
    /// </summary>
    public TestimoXSourceQueryTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<SourceQueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var requestedRuleNames = reader.DistinctStringArray("rule_names");
            var ruleNamePatterns = reader.DistinctStringArray("rule_name_patterns");
            if (ruleNamePatterns.Count > TestimoXRuleSelectionHelper.MaxRuleNamePatterns) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(
                    $"rule_name_patterns supports at most {TestimoXRuleSelectionHelper.MaxRuleNamePatterns} values.");
            }

            var includeDisabled = reader.Boolean("include_disabled", defaultValue: true);
            var includeHidden = reader.Boolean("include_hidden", defaultValue: true);
            var includeDeprecated = reader.Boolean("include_deprecated", defaultValue: true);
            var searchText = reader.OptionalString("search_text");

            var requestedSourceTypes = reader.DistinctStringArray("source_types");
            if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(requestedSourceTypes, out var sourceTypeFilter, out var sourceTypeError)) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(sourceTypeError ?? "Invalid source_types argument.");
            }

            if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                    reader.OptionalString("rule_origin"),
                    out var ruleOrigin,
                    out var ruleOriginError)) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(ruleOriginError ?? "Invalid rule_origin argument.");
            }

            var requestedStates = reader.DistinctStringArray("migration_states");
            if (!TestimoXRuleInventoryHelper.TryParseMigrationStates(requestedStates, out var migrationStateFilter, out var migrationStateError)) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(migrationStateError ?? "Invalid migration_states argument.");
            }

            var requestedCategories = reader.DistinctStringArray("categories");
            var requestedTags = reader.DistinctStringArray("tags");
            var hasSelectorFilters =
                ruleNamePatterns.Count > 0 ||
                !string.IsNullOrWhiteSpace(searchText) ||
                requestedCategories.Count > 0 ||
                requestedTags.Count > 0 ||
                sourceTypeFilter is { Count: > 0 } ||
                migrationStateFilter is { Count: > 0 } ||
                !string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase);
            if (requestedRuleNames.Count == 0 && !hasSelectorFilters) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(
                    "Provide at least one selection input: rule_names, rule_name_patterns, search_text, categories, tags, source_types, rule_origin, or migration_states.");
            }

            var profileName = reader.OptionalString("profile");
            var profile = RuleSelectionProfileCatalog.Parse(profileName);
            if (!string.IsNullOrWhiteSpace(profileName) && !profile.HasValue) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(
                    $"Invalid profile '{profileName}'. Supported values: {string.Join(", ", GetProfileNames())}.");
            }

            var powerShellRulesDirectory = reader.OptionalString("powershell_rules_directory");
            var includeResources = reader.Boolean("include_resources", defaultValue: true);
            var includeLinks = reader.Boolean("include_links", defaultValue: true);
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog) ?? Math.Min(DefaultPageSize, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<SourceQueryRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<SourceQueryRequest>.Success(new SourceQueryRequest(
                RequestedRuleNames: requestedRuleNames,
                RuleNamePatterns: ruleNamePatterns,
                IncludeDisabled: includeDisabled,
                IncludeHidden: includeHidden,
                IncludeDeprecated: includeDeprecated,
                SearchText: searchText,
                SourceTypeFilter: sourceTypeFilter,
                RuleOrigin: ruleOrigin,
                MigrationStateFilter: migrationStateFilter,
                RequestedCategories: requestedCategories,
                RequestedTags: requestedTags,
                Profile: profile,
                PowerShellRulesDirectory: powerShellRulesDirectory,
                IncludeResources: includeResources,
                IncludeLinks: includeLinks,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<SourceQueryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_source_query." },
                isTransient: false);
        }

        var usingExternalDirectory = !string.IsNullOrWhiteSpace(context.Request.PowerShellRulesDirectory);
        HashSet<string>? builtinRuleNames = null;
        var runner = new TestimoRunner();
        var discovery = await TryDiscoverRulesAsync(
            runner,
            context.Request.PowerShellRulesDirectory,
            cancellationToken,
            defaultErrorMessage: "TestimoX source discovery failed.").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(discovery.ErrorResponse)) {
            return discovery.ErrorResponse!;
        }

        var discovered = discovery.Rules ?? new List<Rule>();
        if (usingExternalDirectory) {
            var builtinDiscovery = await TryDiscoverBuiltinRuleNamesAsync(
                cancellationToken,
                defaultErrorMessage: "TestimoX builtin rule discovery failed.").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(builtinDiscovery.ErrorResponse)) {
                return builtinDiscovery.ErrorResponse!;
            }

            builtinRuleNames = builtinDiscovery.RuleNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var availableByName = discovered
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Name))
            .ToDictionary(static rule => rule.Name, StringComparer.OrdinalIgnoreCase);
        var unknown = context.Request.RequestedRuleNames
            .Where(name => !availableByName.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unknown TestimoX rule name(s): {string.Join(", ", unknown)}.",
                hints: new[] { "Call testimox_rules_list first to discover valid rule names." },
                isTransient: false);
        }

        IReadOnlyList<RuleInventoryEntry> inventory;
        IReadOnlyList<RuleOverview> overview;
        try {
            inventory = await RuleIntrospection.GetInventoryAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: context.Request.PowerShellRulesDirectory,
                profile: context.Request.Profile).ConfigureAwait(false);
            overview = await RuleIntrospection.GetOverviewAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: context.Request.PowerShellRulesDirectory).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "TestimoX source metadata discovery failed.");
        }

        var inventoryByName = inventory.ToDictionary(static row => row.Name, StringComparer.OrdinalIgnoreCase);
        var overviewByName = overview.ToDictionary(static row => row.Name, StringComparer.OrdinalIgnoreCase);

        IEnumerable<Rule> filtered = TestimoXRuleSelectionHelper.ApplyVisibilityFilters(
            discovered,
            includeDisabled: context.Request.IncludeDisabled,
            includeHidden: context.Request.IncludeHidden,
            includeDeprecated: context.Request.IncludeDeprecated);
        filtered = TestimoXRuleSelectionHelper.ApplySharedFilters(
            filtered,
            context.Request.SearchText,
            context.Request.RequestedCategories,
            context.Request.RequestedTags,
            context.Request.SourceTypeFilter,
            context.Request.RuleOrigin,
            usingExternalDirectory,
            builtinRuleNames);

        if (context.Request.MigrationStateFilter is { Count: > 0 }) {
            filtered = filtered.Where(rule =>
                inventoryByName.TryGetValue(rule.Name ?? string.Empty, out var row)
                && context.Request.MigrationStateFilter.Contains(TestimoXRuleInventoryHelper.ToMigrationStateName(row.State)));
        }

        var hasSelectorFilters =
            context.Request.RuleNamePatterns.Count > 0 ||
            !string.IsNullOrWhiteSpace(context.Request.SearchText) ||
            context.Request.RequestedCategories.Count > 0 ||
            context.Request.RequestedTags.Count > 0 ||
            context.Request.SourceTypeFilter is { Count: > 0 } ||
            context.Request.MigrationStateFilter is { Count: > 0 } ||
            !string.Equals(context.Request.RuleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase);

        var selectedRules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in context.Request.RequestedRuleNames) {
            selectedRules[name] = availableByName[name];
        }

        if (hasSelectorFilters) {
            foreach (var rule in filtered) {
                if (context.Request.RuleNamePatterns.Count > 0
                    && !TestimoXRuleSelectionHelper.MatchesAnyPattern(rule, context.Request.RuleNamePatterns)) {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.Name)) {
                    selectedRules[rule.Name] = rule;
                }
            }
        }

        var matchedRows = selectedRules.Values
            .OrderBy(static rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .Select(rule => BuildRow(
                rule,
                overviewByName,
                inventoryByName,
                usingExternalDirectory,
                builtinRuleNames,
                context.Request.IncludeResources,
                context.Request.IncludeLinks))
            .ToList();

        var offset = context.Request.Offset;
        if (offset > matchedRows.Count) {
            offset = matchedRows.Count;
        }

        var rows = matchedRows
            .Skip(offset)
            .Take(context.Request.PageSize)
            .ToList();
        var truncatedByPage = offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new TestimoSourceQueryResult(
            DiscoveredCount: discovered.Count,
            MatchedCount: matchedRows.Count,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: context.Request.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: truncatedByPage,
            Truncated: truncatedByPage,
            Profile: context.Request.Profile?.ToString() ?? string.Empty,
            Rules: rows);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rules_view",
            title: "TestimoX source query",
            maxTop: Math.Max(context.Request.PageSize, rows.Count),
            baseTruncated: truncatedByPage,
            scanned: discovered.Count,
            metaMutate: meta => {
                meta.Add("matched_count", matchedRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                meta.Add("page_size", context.Request.PageSize);
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                if (context.Request.Profile.HasValue) {
                    meta.Add("profile", context.Request.Profile.Value.ToString());
                }
                meta.Add("truncated_by_page", truncatedByPage);
            });
    }

    private static TestimoSourceQueryRow BuildRow(
        Rule rule,
        IReadOnlyDictionary<string, RuleOverview> overviewByName,
        IReadOnlyDictionary<string, RuleInventoryEntry> inventoryByName,
        bool usingExternalDirectory,
        HashSet<string>? builtinRuleNames,
        bool includeResources,
        bool includeLinks) {
        overviewByName.TryGetValue(rule.Name ?? string.Empty, out var overview);
        inventoryByName.TryGetValue(rule.Name ?? string.Empty, out var inventory);

        var resources = includeResources
            ? (rule.Guidance?.References ?? rule.Source?.Details?.Resources ?? new List<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var links = includeLinks
            ? (rule.Links ?? new List<RuleLink>())
                .Where(static link => link is not null && !string.IsNullOrWhiteSpace(link.TargetRuleName))
                .Select(link => new TestimoSourceLink(
                    Relation: link.Relation.ToString(),
                    TargetRuleName: link.TargetRuleName,
                    Note: link.Note ?? string.Empty))
                .ToArray()
            : Array.Empty<TestimoSourceLink>();

        return new TestimoSourceQueryRow(
            RuleName: rule.Name ?? string.Empty,
            DisplayName: rule.DisplayName ?? string.Empty,
            Summary: overview?.Summary ?? string.Empty,
            Description: rule.Description ?? string.Empty,
            SourceDescription: rule.Source?.Details?.Description ?? string.Empty,
            SourceType: TestimoXRuleSelectionHelper.GetSourceType(rule),
            RuleOrigin: TestimoXRuleSelectionHelper.ResolveRuleOrigin(rule, usingExternalDirectory, builtinRuleNames),
            EnumMemberName: overview?.EnumMemberName ?? string.Empty,
            EnumQualifiedName: overview?.EnumQualifiedName ?? string.Empty,
            MigrationState: inventory is null ? string.Empty : TestimoXRuleInventoryHelper.ToMigrationStateName(inventory.State),
            AssessmentArea: inventory?.AssessmentArea ?? string.Empty,
            Enabled: rule.Enable,
            Visibility: rule.Visibility.ToString(),
            IsDeprecated: rule.IsDeprecated,
            Scope: rule.Scope.ToString(),
            PermissionRequired: rule.PermissionRequired.ToString(),
            Cost: rule.Cost.ToString(),
            RequiredModules: (rule.RequiredModules ?? Array.Empty<PowerShellModule>())
                .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.Name))
                .Select(static module => module.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SupportedSystems: (rule.SupportedSystems ?? Array.Empty<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Categories: (rule.Category ?? Array.Empty<Category>())
                .Select(static value => value.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Tags: ((IEnumerable<string>)(rule.Tags ?? new List<string>()))
                .Concat(rule.Source?.Details?.Tags ?? Enumerable.Empty<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Resources: resources,
            Supersedes: inventory?.Supersedes ?? Array.Empty<string>(),
            SupersededBy: inventory?.SupersededBy ?? Array.Empty<string>(),
            Links: links,
            LinkCount: links.Length);
    }

    private static string[] GetProfileNames() {
        return RuleSelectionProfileCatalog.GetProfiles()
            .Select(static profile => profile.Profile.ToString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record TestimoSourceQueryResult(
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        string Profile,
        IReadOnlyList<TestimoSourceQueryRow> Rules);

    private sealed record TestimoSourceQueryRow(
        string RuleName,
        string DisplayName,
        string Summary,
        string Description,
        string SourceDescription,
        string SourceType,
        string RuleOrigin,
        string EnumMemberName,
        string EnumQualifiedName,
        string MigrationState,
        string AssessmentArea,
        bool Enabled,
        string Visibility,
        bool IsDeprecated,
        string Scope,
        string PermissionRequired,
        string Cost,
        IReadOnlyList<string> RequiredModules,
        IReadOnlyList<string> SupportedSystems,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Resources,
        IReadOnlyList<string> Supersedes,
        IReadOnlyList<string> SupersededBy,
        IReadOnlyList<TestimoSourceLink> Links,
        int LinkCount);

    private sealed record TestimoSourceLink(
        string Relation,
        string TargetRuleName,
        string Note);
}
