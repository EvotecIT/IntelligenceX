using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Baselines.Crosswalk;
using TestimoX.Definitions;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Maps TestimoX rules to vendor baseline controls and documentation cross-references.
/// </summary>
public sealed class TestimoXBaselineCrosswalkTool : TestimoXToolBase, ITool {
    private const int DefaultPageSize = 25;

    private sealed record BaselineCrosswalkRequest(
        IReadOnlyList<string> RequestedRuleNames,
        IReadOnlyList<string> RuleNamePatterns,
        bool IncludeDisabled,
        bool IncludeHidden,
        bool IncludeDeprecated,
        string? SearchText,
        HashSet<string>? SourceTypeFilter,
        string RuleOrigin,
        IReadOnlyList<string> RequestedCategories,
        IReadOnlyList<string> RequestedTags,
        RuleSelectionProfile? Profile,
        string? PowerShellRulesDirectory,
        bool IncludeMatches,
        bool IncludeDocs,
        bool IncludeLinks,
        int PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_baseline_crosswalk",
        "Map TestimoX rules to vendor baselines and documentation cross-references.",
        ToolSchema.Object(
                ("rule_names", ToolSchema.Array(ToolSchema.String(), "Optional explicit rule names to include.")),
                ("rule_name_patterns", ToolSchema.Array(ToolSchema.String("Wildcard pattern matched against rule_name/display_name (for example: *ldap*)."), "Optional wildcard selectors.")),
                ("include_disabled", ToolSchema.Boolean("Include rules marked disabled. Default false.")),
                ("include_hidden", ToolSchema.Boolean("Include hidden rules. Default false.")),
                ("include_deprecated", ToolSchema.Boolean("Include deprecated rules. Default true.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across rule name/display/description.")),
                ("source_types", ToolSchema.Array(ToolSchema.String("Rule source type.").Enum(TestimoXRuleSelectionHelper.SourceTypeNames), "Optional source type filters (any-match).")),
                ("rule_origin", ToolSchema.String("Optional origin filter. 'builtin' means bundled rules, 'external' means rules introduced through powershell_rules_directory.").Enum(TestimoXRuleSelectionHelper.RuleOriginNames)),
                ("categories", ToolSchema.Array(ToolSchema.String(), "Optional category-name filters (any-match).")),
                ("tags", ToolSchema.Array(ToolSchema.String(), "Optional tag filters (any-match).")),
                ("profile", ToolSchema.String("Optional curated profile used to shape crosswalk selection.").Enum(GetProfileNames())),
                ("powershell_rules_directory", ToolSchema.String("Optional path to user PowerShell rule scripts.")),
                ("include_matches", ToolSchema.Boolean("Include detailed matched vendor baseline controls. Default true.")),
                ("include_docs", ToolSchema.Boolean("Include documentation cross-references (PingCastle/PurpleKnight/etc.). Default true.")),
                ("include_links", ToolSchema.Boolean("Include logical rule links reported by TestimoX. Default true.")),
                ("page_size", ToolSchema.Integer("Optional number of rules to return in this page. Default 25.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched rules (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "crosswalk",
            "baselines",
            "fallback_hint_keys:search_text,rule_origin,categories,tags,source_types,profile,rule_names,rule_name_patterns"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXBaselineCrosswalkTool"/> class.
    /// </summary>
    public TestimoXBaselineCrosswalkTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<BaselineCrosswalkRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var requestedRuleNames = reader.DistinctStringArray("rule_names");
            var ruleNamePatterns = reader.DistinctStringArray("rule_name_patterns");
            if (ruleNamePatterns.Count > TestimoXRuleSelectionHelper.MaxRuleNamePatterns) {
                return ToolRequestBindingResult<BaselineCrosswalkRequest>.Failure(
                    $"rule_name_patterns supports at most {TestimoXRuleSelectionHelper.MaxRuleNamePatterns} values.");
            }

            var includeDisabled = reader.Boolean("include_disabled", defaultValue: false);
            var includeHidden = reader.Boolean("include_hidden", defaultValue: false);
            var includeDeprecated = reader.Boolean("include_deprecated", defaultValue: true);
            var searchText = reader.OptionalString("search_text");

            var requestedSourceTypes = reader.DistinctStringArray("source_types");
            if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(requestedSourceTypes, out var sourceTypeFilter, out var sourceTypeError)) {
                return ToolRequestBindingResult<BaselineCrosswalkRequest>.Failure(sourceTypeError ?? "Invalid source_types argument.");
            }

            if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                    reader.OptionalString("rule_origin"),
                    out var ruleOrigin,
                    out var ruleOriginError)) {
                return ToolRequestBindingResult<BaselineCrosswalkRequest>.Failure(ruleOriginError ?? "Invalid rule_origin argument.");
            }

            var profileName = reader.OptionalString("profile");
            var profile = RuleSelectionProfileCatalog.Parse(profileName);
            if (!string.IsNullOrWhiteSpace(profileName) && !profile.HasValue) {
                return ToolRequestBindingResult<BaselineCrosswalkRequest>.Failure(
                    $"Invalid profile '{profileName}'. Supported values: {string.Join(", ", GetProfileNames())}.");
            }

            var requestedCategories = reader.DistinctStringArray("categories");
            var requestedTags = reader.DistinctStringArray("tags");
            var powerShellRulesDirectory = reader.OptionalString("powershell_rules_directory");
            var includeMatches = reader.Boolean("include_matches", defaultValue: true);
            var includeDocs = reader.Boolean("include_docs", defaultValue: true);
            var includeLinks = reader.Boolean("include_links", defaultValue: true);
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog) ?? Math.Min(DefaultPageSize, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<BaselineCrosswalkRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<BaselineCrosswalkRequest>.Success(new BaselineCrosswalkRequest(
                RequestedRuleNames: requestedRuleNames,
                RuleNamePatterns: ruleNamePatterns,
                IncludeDisabled: includeDisabled,
                IncludeHidden: includeHidden,
                IncludeDeprecated: includeDeprecated,
                SearchText: searchText,
                SourceTypeFilter: sourceTypeFilter,
                RuleOrigin: ruleOrigin,
                RequestedCategories: requestedCategories,
                RequestedTags: requestedTags,
                Profile: profile,
                PowerShellRulesDirectory: powerShellRulesDirectory,
                IncludeMatches: includeMatches,
                IncludeDocs: includeDocs,
                IncludeLinks: includeLinks,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<BaselineCrosswalkRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_baseline_crosswalk." },
                isTransient: false);
        }

        var usingExternalDirectory = !string.IsNullOrWhiteSpace(context.Request.PowerShellRulesDirectory);
        HashSet<string>? builtinRuleNames = null;
        var runner = new TestimoRunner();
        var discovery = await TryDiscoverRulesAsync(
            runner,
            context.Request.PowerShellRulesDirectory,
            cancellationToken,
            defaultErrorMessage: "TestimoX rule discovery failed.").ConfigureAwait(false);
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

        var profileInfo = context.Request.Profile.HasValue
            ? RuleSelectionProfileCatalog.GetProfiles().FirstOrDefault(profile => profile.Profile == context.Request.Profile.Value)
            : null;
        if (profileInfo is not null) {
            filtered = ApplyProfileFilters(filtered, profileInfo);
        }

        var hasSelectorFilters =
            context.Request.RuleNamePatterns.Count > 0 ||
            !string.IsNullOrWhiteSpace(context.Request.SearchText) ||
            context.Request.RequestedCategories.Count > 0 ||
            context.Request.RequestedTags.Count > 0 ||
            context.Request.SourceTypeFilter is { Count: > 0 } ||
            !string.Equals(context.Request.RuleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Profile.HasValue;

        var selectedRules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in context.Request.RequestedRuleNames) {
            if (availableByName.TryGetValue(name, out var rule) && !selectedRules.ContainsKey(name)) {
                selectedRules[name] = rule;
            }
        }

        var includeFilteredRules = hasSelectorFilters || context.Request.RequestedRuleNames.Count == 0;
        if (includeFilteredRules) {
            foreach (var rule in filtered) {
                if (context.Request.RuleNamePatterns.Count > 0
                    && !TestimoXRuleSelectionHelper.MatchesAnyPattern(rule, context.Request.RuleNamePatterns)) {
                    continue;
                }

                var name = rule.Name ?? string.Empty;
                if (name.Length == 0 || selectedRules.ContainsKey(name)) {
                    continue;
                }

                selectedRules[name] = rule;
            }
        }

        if (!includeFilteredRules && selectedRules.Count == 0) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: "No rules matched the requested selectors.",
                hints: new[] { "Call testimox_rules_list first to inspect available rule names and metadata." },
                isTransient: false);
        }

        var matchedRules = selectedRules.Values
            .OrderBy(static rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var offset = context.Request.Offset;
        if (offset > matchedRules.Count) {
            offset = matchedRules.Count;
        }

        var pageRules = matchedRules
            .Skip(offset)
            .Take(context.Request.PageSize)
            .ToList();
        var truncatedByPage = offset + pageRules.Count < matchedRules.Count;
        var nextOffset = truncatedByPage ? offset + pageRules.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;
        var assessmentAreasByRuleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.Request.Profile.HasValue) {
            try {
                var inventory = await RuleIntrospection.GetInventoryAsync(
                    includeDisabled: true,
                    ct: cancellationToken,
                    powerShellRulesDirectory: context.Request.PowerShellRulesDirectory,
                    profile: context.Request.Profile).ConfigureAwait(false);
                assessmentAreasByRuleName = inventory
                    .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .ToDictionary(
                        static entry => entry.Name,
                        static entry => entry.AssessmentArea ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                return ErrorFromException(ex, "TestimoX profile assessment-area discovery failed.");
            }
        }

        var rows = new List<TestimoBaselineCrosswalkRow>(pageRules.Count);
        foreach (var rule in pageRules) {
            cancellationToken.ThrowIfCancellationRequested();

            RuleCrosswalkReport? report;
            try {
                report = CrosswalkService.Build(rule.Name ?? string.Empty);
            } catch (Exception ex) {
                return ErrorFromException(ex, "TestimoX baseline crosswalk generation failed.");
            }

            var matches = context.Request.IncludeMatches
                ? CrosswalkService.BuildMatches(report).ToArray()
                : Array.Empty<CrosswalkMatchRow>();
            var docs = context.Request.IncludeDocs
                ? CrosswalkService.BuildDocs(report).ToArray()
                : Array.Empty<CrosswalkDocRow>();
            var links = context.Request.IncludeLinks && report is not null
                ? report.Links.Select(static link => new TestimoCrosswalkLink(link.Relation, link.Rule)).ToArray()
                : Array.Empty<TestimoCrosswalkLink>();
            var coverage = report?.Coverage;
            var vendorCounts = BuildVendorCounts(matches, docs);

            rows.Add(new TestimoBaselineCrosswalkRow(
                RuleName: rule.Name ?? string.Empty,
                DisplayName: report?.Display ?? rule.DisplayName ?? rule.Name ?? string.Empty,
                AssessmentArea: assessmentAreasByRuleName.TryGetValue(rule.Name ?? string.Empty, out var assessmentArea) ? assessmentArea : string.Empty,
                SourceType: TestimoXRuleSelectionHelper.GetSourceType(rule),
                RuleOrigin: TestimoXRuleSelectionHelper.ResolveRuleOrigin(rule, usingExternalDirectory, builtinRuleNames),
                Scope: rule.Scope.ToString(),
                CoverageTestimoX: coverage?.TestimoX ?? string.Empty,
                CoverageMSB: coverage?.MSB ?? string.Empty,
                CoverageCIS: coverage?.CIS ?? string.Empty,
                CoverageSTIG: coverage?.STIG ?? string.Empty,
                CoveragePingCastle: coverage?.PingCastle ?? string.Empty,
                CoveragePurpleKnight: coverage?.PurpleKnight ?? string.Empty,
                MatchCount: matches.Length,
                DocCount: docs.Length,
                VendorCounts: vendorCounts,
                Matches: matches,
                Docs: docs,
                Links: links));
        }

        var model = new TestimoBaselineCrosswalkResult(
            DiscoveredCount: discovered.Count,
            MatchedCount: matchedRules.Count,
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
            viewRowsPath: "crosswalk_view",
            title: "TestimoX baseline crosswalk",
            maxTop: Math.Max(context.Request.PageSize, rows.Count),
            baseTruncated: truncatedByPage,
            scanned: discovered.Count,
            metaMutate: meta => {
                meta.Add("matched_count", matchedRules.Count);
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

    private static IEnumerable<Rule> ApplyProfileFilters(IEnumerable<Rule> rules, RuleSelectionProfileInfo profile) {
        var filtered = rules;

        if (!profile.IncludeHidden) {
            filtered = filtered.Where(static rule => rule.Visibility != RuleVisibility.Hidden);
        }
        if (profile.ExcludeDeprecated) {
            filtered = filtered.Where(static rule => !rule.IsDeprecated);
        }
        if (profile.IncludeCategories.Count > 0) {
            var includeCategories = new HashSet<string>(profile.IncludeCategories.Select(static value => value.ToString()), StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Category.Any(category => includeCategories.Contains(category.ToString())));
        }
        if (profile.ExcludeCategories.Count > 0) {
            var excludeCategories = new HashSet<string>(profile.ExcludeCategories.Select(static value => value.ToString()), StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => !rule.Category.Any(category => excludeCategories.Contains(category.ToString())));
        }
        if (profile.IncludeTags.Count > 0) {
            var includeTags = new HashSet<string>(profile.IncludeTags, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => EnumerateRuleTags(rule).Any(includeTags.Contains));
        }
        if (profile.ExcludeTags.Count > 0) {
            var excludeTags = new HashSet<string>(profile.ExcludeTags, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => !EnumerateRuleTags(rule).Any(excludeTags.Contains));
        }
        if (profile.ExcludeHeavy) {
            filtered = filtered.Where(static rule => rule.Cost < RuleCost.Heavy);
        }
        if (profile.MaxCost.HasValue) {
            var maxCost = profile.MaxCost.Value;
            filtered = filtered.Where(rule => rule.Cost <= maxCost);
        }

        return filtered;
    }

    private static IEnumerable<string> EnumerateRuleTags(Rule rule) {
        return ((IEnumerable<string>)(rule.Tags ?? new List<string>()))
            .Concat(rule.Source?.Details?.Tags ?? Enumerable.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> BuildVendorCounts(
        IReadOnlyList<CrosswalkMatchRow> matches,
        IReadOnlyList<CrosswalkDocRow> docs) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches) {
            var vendor = (match.Vendor ?? string.Empty).Trim();
            if (vendor.Length == 0) {
                continue;
            }

            counts[vendor] = counts.TryGetValue(vendor, out var current) ? current + 1 : 1;
        }

        foreach (var doc in docs) {
            var vendor = (doc.Source ?? string.Empty).Trim();
            if (vendor.Length == 0) {
                continue;
            }

            counts[vendor] = counts.TryGetValue(vendor, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    private static string[] GetProfileNames() {
        return RuleSelectionProfileCatalog.GetProfiles()
            .Select(static profile => profile.Profile.ToString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record TestimoBaselineCrosswalkResult(
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
        IReadOnlyList<TestimoBaselineCrosswalkRow> Rules);

    private sealed record TestimoBaselineCrosswalkRow(
        string RuleName,
        string DisplayName,
        string AssessmentArea,
        string SourceType,
        string RuleOrigin,
        string Scope,
        string CoverageTestimoX,
        string CoverageMSB,
        string CoverageCIS,
        string CoverageSTIG,
        string CoveragePingCastle,
        string CoveragePurpleKnight,
        int MatchCount,
        int DocCount,
        IReadOnlyDictionary<string, int> VendorCounts,
        IReadOnlyList<CrosswalkMatchRow> Matches,
        IReadOnlyList<CrosswalkDocRow> Docs,
        IReadOnlyList<TestimoCrosswalkLink> Links);

    private sealed record TestimoCrosswalkLink(
        string Relation,
        string Rule);
}
