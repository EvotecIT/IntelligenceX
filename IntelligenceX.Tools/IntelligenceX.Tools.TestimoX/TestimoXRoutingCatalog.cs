using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXRoutingCatalog {
    public const string SecurityPostureDomainIntentFamily = "security_posture";
    public const string SecurityPostureDomainIntentActionId = "act_domain_scope_security_posture";

    private static readonly string[] RunsListFallbackHintKeys = { "store_directory", "run_id_contains", "completed_only" };
    private static readonly string[] RunSummaryFallbackSelectionKeys = { "store_directory", "run_id" };
    private static readonly string[] RunSummaryFallbackHintKeys = { "store_directory", "run_id", "scope_group", "rule_name_contains", "scope_id_contains" };
    private static readonly string[] BaselinesListFallbackHintKeys = { "search_text", "vendor_ids", "product_ids", "version_wildcard", "baseline_ids", "id_patterns" };
    private static readonly string[] BaselineCompareFallbackHintKeys = { "product_id", "vendor_ids", "version_wildcard", "latest_only", "only_diff", "search_text" };
    private static readonly string[] SourceQueryFallbackSelectionKeys = { "search_text", "rule_names", "rule_name_patterns", "categories", "tags", "source_types", "rule_origin", "migration_states" };
    private static readonly string[] SourceQueryFallbackHintKeys = { "search_text", "rule_origin", "rule_names", "rule_name_patterns", "categories", "tags", "source_types", "migration_states", "profile" };
    private static readonly string[] BaselineCrosswalkFallbackHintKeys = { "search_text", "rule_origin", "categories", "tags", "source_types", "profile", "rule_names", "rule_name_patterns" };
    private static readonly string[] RuleInventoryFallbackHintKeys = { "search_text", "rule_origin", "categories", "tags", "source_types", "migration_states", "profile" };
    private static readonly string[] RulesListFallbackHintKeys = { "search_text", "rule_origin", "categories", "tags", "source_types" };
    private static readonly string[] RulesRunFallbackSelectionKeys = { "search_text", "rule_names", "rule_name_patterns", "categories", "tags", "source_types", "rule_origin" };
    private static readonly string[] RulesRunFallbackHintKeys = { "search_text", "rule_origin", "rule_names", "rule_name_patterns", "categories", "tags", "source_types" };

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["testimox_runs_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_run_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_baselines_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_baseline_compare"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_profiles_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_rule_inventory"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_source_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_baseline_crosswalk"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_rules_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_rules_run"] = ToolRoutingTaxonomy.RoleOperational
        };

    public static readonly string[] SetupHintKeys = {
        "search_text",
        "categories",
        "tags",
        "source_types",
        "rule_origin"
    };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "testimox",
        "testimo",
        "baseline",
        "security",
        "posture"
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "TestimoX");
    }

    public static string ResolveDomainIntentFamily(string toolName, string? explicitFamily) {
        if (!string.IsNullOrWhiteSpace(explicitFamily)) {
            return explicitFamily!;
        }

        return string.Equals(toolName, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)
            ? SecurityPostureDomainIntentFamily
            : string.Empty;
    }

    public static string ResolveDomainIntentActionId(string toolName, string? explicitActionId) {
        if (!string.IsNullOrWhiteSpace(explicitActionId)) {
            return explicitActionId!;
        }

        return string.Equals(toolName, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)
            ? SecurityPostureDomainIntentActionId
            : string.Empty;
    }

    public static IReadOnlyList<string> ResolveFallbackSelectionKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return toolName switch {
            "testimox_run_summary" => RunSummaryFallbackSelectionKeys,
            "testimox_source_query" => SourceQueryFallbackSelectionKeys,
            "testimox_rules_run" => RulesRunFallbackSelectionKeys,
            _ => Array.Empty<string>()
        };
    }

    public static IReadOnlyList<string> ResolveFallbackHintKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return toolName switch {
            "testimox_runs_list" => RunsListFallbackHintKeys,
            "testimox_run_summary" => RunSummaryFallbackHintKeys,
            "testimox_baselines_list" => BaselinesListFallbackHintKeys,
            "testimox_baseline_compare" => BaselineCompareFallbackHintKeys,
            "testimox_source_query" => SourceQueryFallbackHintKeys,
            "testimox_baseline_crosswalk" => BaselineCrosswalkFallbackHintKeys,
            "testimox_rule_inventory" => RuleInventoryFallbackHintKeys,
            "testimox_rules_list" => RulesListFallbackHintKeys,
            "testimox_rules_run" => RulesRunFallbackHintKeys,
            _ => Array.Empty<string>()
        };
    }

    public static bool RequiresSelectionForFallback(bool explicitRequiresSelection, IReadOnlyList<string> fallbackSelectionKeys) {
        return explicitRequiresSelection || fallbackSelectionKeys.Count > 0;
    }
}
