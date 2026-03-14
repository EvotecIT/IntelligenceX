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
/// Lists migration-oriented TestimoX rule inventory, including authoritative vs legacy state.
/// </summary>
public sealed class TestimoXRuleInventoryTool : TestimoXToolBase, ITool {
    private sealed record RuleInventoryRequest(
        bool IncludeDisabled,
        bool IncludeHidden,
        bool IncludeDeprecated,
        bool EnabledOnly,
        string? SearchText,
        HashSet<string>? SourceTypeFilter,
        string RuleOrigin,
        HashSet<string>? MigrationStateFilter,
        IReadOnlyList<string> RequestedCategories,
        IReadOnlyList<string> RequestedTags,
        RuleSelectionProfile? Profile,
        string? PowerShellRulesDirectory,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_rule_inventory",
        "List TestimoX rule inventory with migration state, assessment area, and authoritative-vs-legacy metadata.",
        ToolSchema.Object(
                ("include_disabled", ToolSchema.Boolean("Include rules disabled by default. Default true.")),
                ("include_hidden", ToolSchema.Boolean("Include hidden rules. Default true.")),
                ("include_deprecated", ToolSchema.Boolean("Include deprecated rules. Default true.")),
                ("enabled_only", ToolSchema.Boolean("Return only enabled rules after other filters. Default false.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across rule name/display/assessment area.")),
                ("source_types", ToolSchema.Array(ToolSchema.String("Rule source type.").Enum(TestimoXRuleSelectionHelper.SourceTypeNames), "Optional source type filters (any-match).")),
                ("rule_origin", ToolSchema.String("Optional origin filter. 'builtin' means bundled rules, 'external' means rules introduced through powershell_rules_directory.").Enum(TestimoXRuleSelectionHelper.RuleOriginNames)),
                ("migration_states", ToolSchema.Array(ToolSchema.String("Inventory migration state.").Enum(TestimoXRuleInventoryHelper.MigrationStateNames), "Optional migration-state filters (any-match).")),
                ("categories", ToolSchema.Array(ToolSchema.String(), "Optional category-name filters (any-match).")),
                ("tags", ToolSchema.Array(ToolSchema.String(), "Optional tag filters (any-match).")),
                ("profile", ToolSchema.String("Optional curated profile used to classify assessment areas.").Enum(GetProfileNames())),
                ("powershell_rules_directory", ToolSchema.String("Optional path to user PowerShell rule scripts.")),
                ("page_size", ToolSchema.Integer("Optional number of inventory rows to return in this page.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched inventory rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "rules",
            "inventory"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRuleInventoryTool"/> class.
    /// </summary>
    public TestimoXRuleInventoryTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<RuleInventoryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var includeDisabled = reader.Boolean("include_disabled", defaultValue: true);
            var includeHidden = reader.Boolean("include_hidden", defaultValue: true);
            var includeDeprecated = reader.Boolean("include_deprecated", defaultValue: true);
            var enabledOnly = reader.Boolean("enabled_only", defaultValue: false);
            var searchText = reader.OptionalString("search_text");

            var requestedSourceTypes = reader.DistinctStringArray("source_types");
            if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(requestedSourceTypes, out var sourceTypeFilter, out var sourceTypeError)) {
                return ToolRequestBindingResult<RuleInventoryRequest>.Failure(sourceTypeError ?? "Invalid source_types argument.");
            }

            if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                    reader.OptionalString("rule_origin"),
                    out var ruleOrigin,
                    out var ruleOriginError)) {
                return ToolRequestBindingResult<RuleInventoryRequest>.Failure(ruleOriginError ?? "Invalid rule_origin argument.");
            }

            var requestedStates = reader.DistinctStringArray("migration_states");
            if (!TestimoXRuleInventoryHelper.TryParseMigrationStates(requestedStates, out var migrationStateFilter, out var migrationStateError)) {
                return ToolRequestBindingResult<RuleInventoryRequest>.Failure(migrationStateError ?? "Invalid migration_states argument.");
            }

            var profileName = reader.OptionalString("profile");
            var profile = RuleSelectionProfileCatalog.Parse(profileName);
            if (!string.IsNullOrWhiteSpace(profileName) && !profile.HasValue) {
                return ToolRequestBindingResult<RuleInventoryRequest>.Failure(
                    $"Invalid profile '{profileName}'. Supported values: {string.Join(", ", GetProfileNames())}.");
            }

            var requestedCategories = reader.DistinctStringArray("categories");
            var requestedTags = reader.DistinctStringArray("tags");
            var powerShellRulesDirectory = reader.OptionalString("powershell_rules_directory");
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<RuleInventoryRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<RuleInventoryRequest>.Success(new RuleInventoryRequest(
                IncludeDisabled: includeDisabled,
                IncludeHidden: includeHidden,
                IncludeDeprecated: includeDeprecated,
                EnabledOnly: enabledOnly,
                SearchText: searchText,
                SourceTypeFilter: sourceTypeFilter,
                RuleOrigin: ruleOrigin,
                MigrationStateFilter: migrationStateFilter,
                RequestedCategories: requestedCategories,
                RequestedTags: requestedTags,
                Profile: profile,
                PowerShellRulesDirectory: powerShellRulesDirectory,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<RuleInventoryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_rule_inventory." },
                isTransient: false);
        }

        var usingExternalDirectory = !string.IsNullOrWhiteSpace(context.Request.PowerShellRulesDirectory);
        HashSet<string>? builtinRuleNames = null;
        if (usingExternalDirectory) {
            var builtinDiscovery = await TryDiscoverBuiltinRuleNamesAsync(
                cancellationToken,
                defaultErrorMessage: "TestimoX builtin rule discovery failed.").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(builtinDiscovery.ErrorResponse)) {
                return builtinDiscovery.ErrorResponse!;
            }

            builtinRuleNames = builtinDiscovery.RuleNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyList<RuleInventoryEntry> inventory;
        try {
            inventory = await RuleIntrospection.GetInventoryAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: context.Request.PowerShellRulesDirectory,
                profile: context.Request.Profile).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "TestimoX rule inventory discovery failed.");
        }

        IEnumerable<RuleInventoryEntry> filtered = inventory;
        if (!context.Request.IncludeDisabled || context.Request.EnabledOnly) {
            filtered = filtered.Where(static row => row.Enabled);
        }
        if (!context.Request.IncludeHidden) {
            filtered = filtered.Where(static row => row.Visibility != RuleVisibility.Hidden);
        }
        if (!context.Request.IncludeDeprecated) {
            filtered = filtered.Where(static row => !row.Deprecated);
        }
        if (context.Request.SourceTypeFilter is { Count: > 0 }) {
            filtered = filtered.Where(row => context.Request.SourceTypeFilter.Contains(row.Type.ToString()));
        }
        if (context.Request.MigrationStateFilter is { Count: > 0 }) {
            filtered = filtered.Where(row => context.Request.MigrationStateFilter.Contains(TestimoXRuleInventoryHelper.ToMigrationStateName(row.State)));
        }
        if (context.Request.RequestedCategories.Count > 0) {
            var requestedCategories = new HashSet<string>(context.Request.RequestedCategories, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(row => row.Categories.Any(category => requestedCategories.Contains(category.ToString())));
        }
        if (context.Request.RequestedTags.Count > 0) {
            var requestedTags = new HashSet<string>(context.Request.RequestedTags, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(row => row.Tags.Any(tag => requestedTags.Contains(tag)));
        }
        if (!string.Equals(context.Request.RuleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase)) {
            filtered = filtered.Where(row => string.Equals(
                ResolveRuleOrigin(row, usingExternalDirectory, builtinRuleNames),
                context.Request.RuleOrigin,
                StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.SearchText)) {
            var search = context.Request.SearchText.Trim();
            filtered = filtered.Where(row =>
                ContainsOrdinalIgnoreCase(row.Name, search)
                || ContainsOrdinalIgnoreCase(row.DisplayName, search)
                || ContainsOrdinalIgnoreCase(row.AssessmentArea, search));
        }

        var matchedRows = filtered
            .OrderBy(static row => row.State)
            .ThenBy(static row => row.Type)
            .ThenBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TestimoRuleInventoryRow(
                RuleName: row.Name,
                DisplayName: row.DisplayName,
                SourceType: row.Type.ToString(),
                RuleOrigin: ResolveRuleOrigin(row, usingExternalDirectory, builtinRuleNames),
                Enabled: row.Enabled,
                Visibility: row.Visibility.ToString(),
                IsDeprecated: row.Deprecated,
                MigrationState: TestimoXRuleInventoryHelper.ToMigrationStateName(row.State),
                SuppressedByDefault: row.SuppressedByDefault,
                AssessmentArea: row.AssessmentArea ?? string.Empty,
                Categories: row.Categories.Select(static value => value.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                Tags: row.Tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                Supersedes: row.Supersedes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                SupersededBy: row.SupersededBy.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToList();

        var offset = context.Request.Offset;
        if (offset > matchedRows.Count) {
            offset = matchedRows.Count;
        }

        var pageRows = matchedRows.Skip(offset);
        var rows = context.Request.PageSize.HasValue
            ? pageRows.Take(context.Request.PageSize.Value).ToList()
            : pageRows.ToList();
        var truncatedByPage = context.Request.PageSize.HasValue && offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new TestimoRuleInventoryResult(
            DiscoveredCount: inventory.Count,
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
            title: "TestimoX rule inventory",
            maxTop: Math.Max(Options.MaxRulesInCatalog, matchedRows.Count),
            baseTruncated: truncatedByPage,
            scanned: inventory.Count,
            metaMutate: meta => {
                meta.Add("matched_count", matchedRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                if (context.Request.PageSize.HasValue) {
                    meta.Add("page_size", context.Request.PageSize.Value);
                }
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

    private static string[] GetProfileNames() {
        return RuleSelectionProfileCatalog.GetProfiles()
            .Select(static profile => profile.Profile.ToString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveRuleOrigin(
        RuleInventoryEntry row,
        bool usingExternalDirectory,
        HashSet<string>? builtinRuleNames) {
        if (!usingExternalDirectory) {
            return TestimoXRuleSelectionHelper.RuleOriginBuiltin;
        }

        if (row.Type != RuleSourceType.PowerShell) {
            return TestimoXRuleSelectionHelper.RuleOriginBuiltin;
        }

        return builtinRuleNames is { Count: > 0 } && builtinRuleNames.Contains(row.Name)
            ? TestimoXRuleSelectionHelper.RuleOriginBuiltin
            : TestimoXRuleSelectionHelper.RuleOriginExternal;
    }

    private static bool ContainsOrdinalIgnoreCase(string? value, string search) {
        return !string.IsNullOrWhiteSpace(value)
            && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed record TestimoRuleInventoryResult(
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int? PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        string Profile,
        IReadOnlyList<TestimoRuleInventoryRow> Rules);

    private sealed record TestimoRuleInventoryRow(
        string RuleName,
        string DisplayName,
        string SourceType,
        string RuleOrigin,
        bool Enabled,
        string Visibility,
        bool IsDeprecated,
        string MigrationState,
        bool SuppressedByDefault,
        string AssessmentArea,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Supersedes,
        IReadOnlyList<string> SupersededBy);
}
