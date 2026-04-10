using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private const int MaxSyntheticPublicProbeTargets = 5;
    private const int MaxSyntheticPublicAuthoritativeEvidenceQueries = 2;

    internal static bool TryBuildSyntheticPublicProbeReplayForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string rememberedPublicTarget,
        IReadOnlyList<string>? rememberedPublicHosts,
        out IReadOnlyList<ToolCall> calls) {
        return TryBuildSyntheticPublicProbeReplayCalls(
            userRequest,
            toolDefinitions,
            rememberedPublicTarget,
            rememberedPublicHosts,
            includeResolverChecks: RequestNeedsPublicResolverContinuationFromPrompt(userRequest),
            out calls,
            out _);
    }

    internal static bool TryBuildSyntheticPublicDomainOperationalReplayForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        string rememberedPublicTarget,
        out IReadOnlyList<ToolCall> calls) {
        return TryBuildSyntheticPublicDomainOperationalReplayCalls(
            userRequest,
            toolDefinitions,
            preferredFamily,
            rememberedPublicTarget,
            out calls,
            out _);
    }

    internal static bool TryBuildSyntheticAdEventReplayForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<string> rememberedAdHosts,
        out IReadOnlyList<ToolCall> calls) {
        return TryBuildSyntheticScenarioContractAdEventReplayCalls(
            userRequest,
            toolDefinitions,
            preferredFamily,
            rememberedAdHosts,
            out calls,
            out _);
    }

    internal static bool TryBuildSyntheticAdEventPlatformFallbackReplayForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<ToolCall> priorCalls,
        IReadOnlyList<ToolOutput> priorOutputs,
        out IReadOnlyList<ToolCall> calls) {
        return TryBuildSyntheticAdEventPlatformFallbackReplayCalls(
            userRequest,
            toolDefinitions,
            preferredFamily,
            priorCalls,
            priorOutputs,
            out calls,
            out _);
    }

    internal static bool TryBuildSyntheticAdMonitoringReplayForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<string> rememberedAdHosts,
        out IReadOnlyList<ToolCall> calls) {
        return TryBuildSyntheticScenarioContractAdMonitoringReplayCalls(
            userRequest,
            toolDefinitions,
            preferredFamily,
            rememberedAdHosts,
            out calls,
            out _);
    }

    internal static bool TryBuildSyntheticAdReplicationReplayForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<string> rememberedAdHosts,
        out IReadOnlyList<ToolCall> calls) {
        return TryBuildSyntheticScenarioContractAdReplicationReplayCalls(
            userRequest,
            toolDefinitions,
            preferredFamily,
            rememberedAdHosts,
            out calls,
            out _);
    }

    internal static bool TryBuildSyntheticDomainIntentBootstrapForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<string>? pendingFamilies,
        string rememberedAdTarget,
        string rememberedPublicTarget,
        out string toolName,
        out string argumentsJson) {
        argumentsJson = string.Empty;
        if (!TryResolveSyntheticDomainIntentBootstrap(
                userRequest,
                toolDefinitions,
                pendingFamilies,
                rememberedAdTarget,
                rememberedPublicTarget,
                out toolName,
                out var arguments,
                out _)) {
            return false;
        }

        argumentsJson = JsonLite.Serialize(arguments);
        return true;
    }

    private sealed partial class ReplSession {
        private bool TryBuildSyntheticDomainIntentReplayCalls(
            string userRequest,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            out IReadOnlyList<ToolCall> calls,
            out string reason) {
            calls = Array.Empty<ToolCall>();
            reason = string.Empty;
            if (TryBuildSyntheticDomainIntentBootstrapCall(userRequest, toolDefinitions, out var bootstrapCall, out reason)) {
                calls = new[] { bootstrapCall };
                return true;
            }

            if (TryBuildSyntheticScenarioContractPublicProbeReplayCalls(userRequest, toolDefinitions, out calls, out reason)) {
                return true;
            }

            if (TryBuildSyntheticScenarioContractAdEventReplayCalls(
                    userRequest,
                    toolDefinitions,
                    _preferredDomainIntentFamily,
                    GetRecentHostTargetsSnapshot(),
                    out calls,
                    out reason)) {
                return true;
            }

            if (TryBuildSyntheticScenarioContractAdMonitoringReplayCalls(
                    userRequest,
                    toolDefinitions,
                    _preferredDomainIntentFamily,
                    GetRecentHostTargetsSnapshot(),
                    out calls,
                    out reason)) {
                return true;
            }

            if (TryBuildSyntheticScenarioContractAdReplicationReplayCalls(
                    userRequest,
                    toolDefinitions,
                    _preferredDomainIntentFamily,
                    GetRecentHostTargetsSnapshot(),
                    out calls,
                    out reason)) {
                return true;
            }

            if (TryBuildSyntheticPublicDomainOperationalReplayCalls(
                    userRequest,
                    toolDefinitions,
                    _preferredDomainIntentFamily,
                    _rememberedPublicDomainTarget,
                    out calls,
                    out reason)) {
                return true;
            }

            return false;
        }

        private bool TryBuildSyntheticScenarioContractPublicProbeReplayCalls(
            string userRequest,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            out IReadOnlyList<ToolCall> calls,
            out string reason) {
            calls = Array.Empty<ToolCall>();
            reason = string.Empty;
            if (toolDefinitions is null
                || toolDefinitions.Count == 0
                || !TryGetPreferredDomainIntentFamily(out var preferredFamily)
                || !string.Equals(preferredFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)
                || !TryParseScenarioExecutionContractRequirements(userRequest, out var requirements)
                || requirements is null) {
                return false;
            }

            var probeRequired = requirements.RequiredTools.Any(pattern =>
                                    PatternMatchesToolName(pattern, "dnsclientx_ping")
                                    || PatternMatchesToolName(pattern, "domaindetective_network_probe"))
                                || requirements.RequiredAnyTools.Any(pattern =>
                                    PatternMatchesToolName(pattern, "dnsclientx_ping")
                                    || PatternMatchesToolName(pattern, "domaindetective_network_probe"))
                                || requirements.MinToolCalls >= 2;
            if (!probeRequired) {
                return false;
            }

            return TryBuildSyntheticPublicProbeReplayCalls(
                userRequest,
                toolDefinitions,
                _rememberedPublicDomainTarget,
                GetRecentHostTargetsSnapshot(),
                includeResolverChecks: requirements.RequiredTools.Any(pattern => PatternMatchesToolName(pattern, "dnsclientx_query"))
                                       || requirements.RequiredAnyTools.Any(pattern => PatternMatchesToolName(pattern, "dnsclientx_query")),
                out calls,
                out reason);
        }

        private bool TryBuildSyntheticDomainIntentBootstrapCall(
            string userRequest,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            out ToolCall call,
            out string reason) {
            call = null!;
            reason = string.Empty;
            if (!TryResolveSyntheticDomainIntentBootstrap(
                    userRequest,
                    toolDefinitions,
                    GetActivePendingDomainIntentClarificationFamilies(),
                    _rememberedAdDomainTarget,
                    _rememberedPublicDomainTarget,
                    out var toolName,
                    out var arguments,
                    out reason)) {
                return false;
            }

            call = BuildSyntheticDomainIntentBootstrapCall(toolName, arguments);
            return true;
        }
    }

    private static ToolCall BuildSyntheticDomainIntentBootstrapCall(string toolName, JsonObject arguments) {
        var callId = "host_domain_intent_" + Guid.NewGuid().ToString("N");
        var serializedArguments = JsonLite.Serialize(arguments);
        var raw = new JsonObject(StringComparer.Ordinal)
            .Add("type", "tool_call")
            .Add("call_id", callId)
            .Add("name", toolName)
            .Add("arguments", serializedArguments);

        return new ToolCall(
            callId: callId,
            name: toolName,
            input: serializedArguments,
            arguments: arguments,
            raw: raw);
    }

    private static bool TryResolveSyntheticDomainIntentBootstrap(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<string>? pendingFamilies,
        string rememberedAdTarget,
        string rememberedPublicTarget,
        out string toolName,
        out JsonObject arguments,
        out string reason) {
        toolName = string.Empty;
        arguments = new JsonObject(StringComparer.Ordinal);
        reason = string.Empty;
        if (toolDefinitions is null
            || toolDefinitions.Count == 0
            || !LooksLikePureDomainIntentSelection(userRequest)
            || !TryResolveDomainIntentFamilySelection(userRequest, toolDefinitions, pendingFamilies, out var family)
            || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return false;
        }

        var rememberedTarget = string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)
            ? string.Empty
            : string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)
                ? NormalizeRememberedDomainTarget(rememberedPublicTarget)
                : string.Empty;
        var resolvedToolName = ResolveDomainIntentBootstrapToolName(normalizedFamily, toolDefinitions, rememberedTarget);
        if (string.IsNullOrWhiteSpace(resolvedToolName)) {
            return false;
        }

        toolName = resolvedToolName;
        var normalizedToolName = resolvedToolName.Trim();
        if (string.Equals(normalizedToolName, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
            if (rememberedTarget.Length == 0) {
                return false;
            }

            arguments = new JsonObject(StringComparer.Ordinal)
                .Add("domain", rememberedTarget);
            reason = "domain_intent_public_domain_summary";
            return true;
        }

        if (string.Equals(normalizedToolName, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            if (rememberedTarget.Length == 0) {
                return false;
            }

            arguments = new JsonObject(StringComparer.Ordinal)
                .Add("name", rememberedTarget);
            reason = "domain_intent_public_dns_query";
            return true;
        }

        if (string.Equals(normalizedToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)) {
            arguments = new JsonObject(StringComparer.Ordinal)
                .Add("discovery_fallback", "current_forest");
            if (rememberedTarget.Length > 0) {
                arguments.Add("domain_name", rememberedTarget);
                reason = "domain_intent_ad_scope_target_inferred";
            } else {
                reason = "domain_intent_ad_scope";
            }

            return true;
        }

        if (string.Equals(normalizedToolName, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "domaindetective_pack_info", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "dnsclientx_pack_info", StringComparison.OrdinalIgnoreCase)) {
            arguments = new JsonObject(StringComparer.Ordinal);
            reason = "domain_intent_bootstrap_empty_args";
            return true;
        }

        return false;
    }

    private static bool TryBuildSyntheticScenarioContractAdEventReplayCalls(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<string> rememberedAdHosts,
        out IReadOnlyList<ToolCall> calls,
        out string reason) {
        calls = Array.Empty<ToolCall>();
        reason = string.Empty;
        var availableToolDefinitions = toolDefinitions ?? Array.Empty<ToolDefinition>();
        if (availableToolDefinitions.Count == 0
            || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(preferredFamily, out var normalizedFamily)
            || !string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)
            || !TryParseSyntheticAdEventReplayRequirements(userRequest, out var requiresEventLog, out var requiredDistinctHosts, out var requiresAllRememberedHosts)) {
            return false;
        }

        if (!requiresEventLog && requiredDistinctHosts <= 1) {
            return false;
        }
        requiredDistinctHosts = Math.Max(2, requiredDistinctHosts);
        var targetDistinctHosts = requiresAllRememberedHosts ? int.MaxValue : requiredDistinctHosts;

        var toolName = ResolveMatchingToolName(availableToolDefinitions, "eventlog_top_events");
        if (toolName.Length == 0) {
            toolName = ResolveMatchingToolName(availableToolDefinitions, "eventlog_live_stats");
        }
        if (toolName.Length == 0) {
            toolName = ResolveMatchingToolName(availableToolDefinitions, "eventlog_live_query");
        }
        if (toolName.Length == 0) {
            return false;
        }

        var distinctHosts = new List<string>(requiredDistinctHosts);
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rememberedAdHosts.Count && distinctHosts.Count < targetDistinctHosts; index++) {
            var normalizedHost = NormalizeSyntheticHostTargetCandidate(rememberedAdHosts[index]);
            if (normalizedHost.Length == 0 || !seenHosts.Add(normalizedHost)) {
                continue;
            }

            distinctHosts.Add(normalizedHost);
        }

        if (!requiresAllRememberedHosts && distinctHosts.Count < requiredDistinctHosts) {
            return false;
        }

        var replayCalls = new List<ToolCall>(distinctHosts.Count);
        for (var index = 0; index < distinctHosts.Count; index++) {
            var arguments = new JsonObject(StringComparer.Ordinal)
                .Add("machine_name", distinctHosts[index]);
            if (string.Equals(toolName, "eventlog_top_events", StringComparison.OrdinalIgnoreCase)) {
                arguments.Add("log_name", "System");
                arguments.Add("max_events", 10);
            } else if (string.Equals(toolName, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase)) {
                arguments.Add("log_name", "System");
            } else if (string.Equals(toolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase)) {
                arguments.Add("log_name", "System");
                arguments.Add("xpath", "*");
                arguments.Add("max_events", 10);
            }

            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(toolName, arguments));
        }

        calls = replayCalls;
        reason = $"domain_intent_ad_eventlog_{distinctHosts.Count}";
        return replayCalls.Count > 0;
    }

    private static bool TryBuildSyntheticAdEventPlatformFallbackReplayCalls(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<ToolCall> priorCalls,
        IReadOnlyList<ToolOutput> priorOutputs,
        out IReadOnlyList<ToolCall> calls,
        out string reason) {
        calls = Array.Empty<ToolCall>();
        reason = string.Empty;
        var availableToolDefinitions = toolDefinitions ?? Array.Empty<ToolDefinition>();
        if (availableToolDefinitions.Count == 0
            || priorCalls is null
            || priorOutputs is null
            || HasMixedDomainIntentSignals(userRequest)
            || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(preferredFamily, out var normalizedFamily)
            || !string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)
            || !TryParseSyntheticAdEventReplayRequirements(userRequest, out var requiresEventLog, out var requiredDistinctHosts, out var requiresAllRememberedHosts)) {
            return false;
        }

        if (!requiresEventLog && requiredDistinctHosts <= 1) {
            return false;
        }

        var platformBlockedHosts = CollectPlatformBlockedEventLogHosts(priorCalls, priorOutputs);
        if (platformBlockedHosts.Count == 0) {
            return false;
        }

        var selectedHosts = new List<string>();
        var requiredHostCount = requiresAllRememberedHosts
            ? platformBlockedHosts.Count
            : Math.Max(2, requiredDistinctHosts);
        for (var index = 0; index < platformBlockedHosts.Count; index++) {
            selectedHosts.Add(platformBlockedHosts[index]);
            if (!requiresAllRememberedHosts && selectedHosts.Count >= requiredHostCount) {
                break;
            }
        }

        if (!requiresAllRememberedHosts && selectedHosts.Count < requiredHostCount) {
            return false;
        }

        var connectivityToolName = ResolveMatchingToolName(availableToolDefinitions, "system_connectivity_probe");
        var telemetryToolName = ResolveMatchingToolName(availableToolDefinitions, "system_windows_update_telemetry");
        if (connectivityToolName.Length == 0 && telemetryToolName.Length == 0) {
            return false;
        }

        var replayCalls = new List<ToolCall>(selectedHosts.Count * 2);
        for (var index = 0; index < selectedHosts.Count; index++) {
            var host = selectedHosts[index];
            if (connectivityToolName.Length > 0) {
                replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                    connectivityToolName,
                    new JsonObject(StringComparer.Ordinal)
                        .Add("computer_name", host)
                        .Add("include_time_sync", true)));
            }

            if (telemetryToolName.Length > 0) {
                replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                    telemetryToolName,
                    new JsonObject(StringComparer.Ordinal)
                        .Add("computer_name", host)
                        .Add("include_event_telemetry", false)));
            }
        }

        if (replayCalls.Count == 0) {
            return false;
        }

        calls = replayCalls;
        reason = $"domain_intent_ad_eventlog_platform_fallback_{selectedHosts.Count}";
        return true;
    }

    private static bool TryBuildSyntheticScenarioContractAdMonitoringReplayCalls(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<string> rememberedAdHosts,
        out IReadOnlyList<ToolCall> calls,
        out string reason) {
        calls = Array.Empty<ToolCall>();
        reason = string.Empty;
        var availableToolDefinitions = toolDefinitions ?? Array.Empty<ToolDefinition>();
        if (availableToolDefinitions.Count == 0
            || HasMixedDomainIntentSignals(userRequest)
            || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(preferredFamily, out var normalizedFamily)
            || !string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)
            || !TryParseSyntheticAdMonitoringReplayRequirements(userRequest, out var requiresLdap, out var requiresAdws, out var requiredDistinctHosts)
            || (!requiresLdap && !requiresAdws)) {
            return false;
        }

        var toolName = ResolveMatchingToolName(availableToolDefinitions, "ad_monitoring_probe_run");
        if (toolName.Length == 0) {
            return false;
        }

        var distinctHosts = new List<string>();
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rememberedAdHosts.Count; index++) {
            var normalizedHost = NormalizeSyntheticHostTargetCandidate(rememberedAdHosts[index]);
            if (normalizedHost.Length == 0 || !seenHosts.Add(normalizedHost)) {
                continue;
            }

            distinctHosts.Add(normalizedHost);
        }

        var replayCalls = new List<ToolCall>(2);
        if (requiresLdap) {
            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                toolName,
                BuildSyntheticAdMonitoringProbeArguments("ldap", distinctHosts, requiredDistinctHosts)));
        }

        if (requiresAdws) {
            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                toolName,
                BuildSyntheticAdMonitoringProbeArguments("adws", distinctHosts, requiredDistinctHosts)));
        }

        if (replayCalls.Count == 0) {
            return false;
        }

        calls = replayCalls;
        reason = requiresLdap && requiresAdws
            ? "domain_intent_ad_monitoring_ldap_adws"
            : requiresLdap
                ? "domain_intent_ad_monitoring_ldap"
                : "domain_intent_ad_monitoring_adws";
        return true;
    }

    private static bool TryBuildSyntheticScenarioContractAdReplicationReplayCalls(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        IReadOnlyList<string> rememberedAdHosts,
        out IReadOnlyList<ToolCall> calls,
        out string reason) {
        calls = Array.Empty<ToolCall>();
        reason = string.Empty;
        var availableToolDefinitions = toolDefinitions ?? Array.Empty<ToolDefinition>();
        if (availableToolDefinitions.Count == 0
            || HasMixedDomainIntentSignals(userRequest)
            || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(preferredFamily, out var normalizedFamily)
            || !string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)
            || !TryParseSyntheticAdReplicationReplayRequirements(userRequest, out var requiresReplication, out var minimumToolCalls)
            || !requiresReplication) {
            return false;
        }

        var toolName = ResolveMatchingToolName(availableToolDefinitions, "ad_monitoring_probe_run");
        if (toolName.Length == 0) {
            return false;
        }

        var distinctHosts = new List<string>();
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rememberedAdHosts.Count; index++) {
            var normalizedHost = NormalizeSyntheticHostTargetCandidate(rememberedAdHosts[index]);
            if (normalizedHost.Length == 0 || !seenHosts.Add(normalizedHost)) {
                continue;
            }

            distinctHosts.Add(normalizedHost);
        }

        if (distinctHosts.Count == 0) {
            return false;
        }

        var batchCount = Math.Max(1, Math.Min(Math.Max(minimumToolCalls, 1), distinctHosts.Count));
        var replayCalls = new List<ToolCall>(batchCount);
        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++) {
            var targets = new JsonArray();
            for (var hostIndex = batchIndex; hostIndex < distinctHosts.Count; hostIndex += batchCount) {
                targets.Add(distinctHosts[hostIndex]);
            }

            if (targets.Count == 0) {
                continue;
            }

            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                toolName,
                new JsonObject(StringComparer.OrdinalIgnoreCase)
                    .Add("probe_kind", "replication")
                    .Add("targets", targets)
                    .Add("include_sysvol", true)
                    .Add("test_ports", true)
                    .Add("test_ping", true)));
        }

        if (replayCalls.Count == 0) {
            return false;
        }

        calls = replayCalls;
        reason = $"domain_intent_ad_replication_{replayCalls.Count}";
        return true;
    }

    private static bool TryParseSyntheticAdEventReplayRequirements(
        string userRequest,
        out bool requiresEventLog,
        out int requiredDistinctHosts,
        out bool requiresAllRememberedHosts) {
        var normalized = (userRequest ?? string.Empty).Trim();
        requiresEventLog = normalized.IndexOf("eventlog_", StringComparison.OrdinalIgnoreCase) >= 0;
        requiredDistinctHosts = 0;
        requiresAllRememberedHosts = false;
        if (normalized.Length == 0) {
            return false;
        }

        var matches = Regex.Matches(
            normalized,
            "(?im)[\"']?(?<key>machine_name|computer_name|host|server|domain_controller)[\"']?\\s*:\\s*(?<value>\\d+)");
        for (var index = 0; index < matches.Count; index++) {
            if (!int.TryParse(matches[index].Groups["value"].Value, out var parsed)) {
                continue;
            }

            requiredDistinctHosts = Math.Max(requiredDistinctHosts, parsed);
        }

        var extractedRequest = ExtractScenarioUserRequestForDomainIntentRouting(userRequest ?? string.Empty);
        requiresAllRememberedHosts =
            extractedRequest.IndexOf("all remaining", StringComparison.OrdinalIgnoreCase) >= 0
            || extractedRequest.IndexOf("remaining discovered dcs", StringComparison.OrdinalIgnoreCase) >= 0
            || extractedRequest.IndexOf("remaining dc", StringComparison.OrdinalIgnoreCase) >= 0;

        return requiresEventLog || requiredDistinctHosts > 1;
    }

    private static bool TryParseSyntheticAdMonitoringReplayRequirements(
        string userRequest,
        out bool requiresLdap,
        out bool requiresAdws,
        out int requiredDistinctHosts) {
        requiresLdap = false;
        requiresAdws = false;
        requiredDistinctHosts = 0;
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.IndexOf("ad_*ldap*", StringComparison.OrdinalIgnoreCase) >= 0) {
            requiresLdap = true;
        }

        if (normalized.IndexOf("ad_*adws*", StringComparison.OrdinalIgnoreCase) >= 0) {
            requiresAdws = true;
        }

        var hostCountMatches = Regex.Matches(
            normalized,
            "(?im)[\"']?(?<key>machine_name|computer_name|host|server|domain_controller|targets|domain_controllers)[\"']?\\s*:\\s*(?<value>\\d+)");
        for (var index = 0; index < hostCountMatches.Count; index++) {
            if (!int.TryParse(hostCountMatches[index].Groups["value"].Value, out var parsed)) {
                continue;
            }

            requiredDistinctHosts = Math.Max(requiredDistinctHosts, parsed);
        }

        var extractedRequest = ExtractScenarioUserRequestForDomainIntentRouting(userRequest ?? string.Empty);
        requiresLdap |= extractedRequest.IndexOf("ldap", StringComparison.OrdinalIgnoreCase) >= 0;
        requiresAdws |= extractedRequest.IndexOf("adws", StringComparison.OrdinalIgnoreCase) >= 0;
        return requiresLdap || requiresAdws;
    }

    private static bool TryParseSyntheticAdReplicationReplayRequirements(
        string userRequest,
        out bool requiresReplication,
        out int minimumToolCalls) {
        requiresReplication = false;
        minimumToolCalls = 0;
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        requiresReplication = normalized.IndexOf("ad_*replication*", StringComparison.OrdinalIgnoreCase) >= 0;
        var extractedRequest = ExtractScenarioUserRequestForDomainIntentRouting(userRequest ?? string.Empty);
        requiresReplication |= extractedRequest.IndexOf("replication", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!requiresReplication) {
            return false;
        }

        var structuredMatch = Regex.Match(
            normalized,
            "(?im)^\\s*min_tool_calls\\s*:\\s*(?<count>\\d+)\\s*$");
        if (structuredMatch.Success && int.TryParse(structuredMatch.Groups["count"].Value, out var structuredCount)) {
            minimumToolCalls = Math.Max(minimumToolCalls, structuredCount);
        }

        var proseMatch = Regex.Match(
            normalized,
            "Minimum tool calls in this turn:\\s*(?<count>\\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (proseMatch.Success && int.TryParse(proseMatch.Groups["count"].Value, out var proseCount)) {
            minimumToolCalls = Math.Max(minimumToolCalls, proseCount);
        }

        return true;
    }

    private static bool PatternSuggestsAdProbeKind(string pattern, string probeKind) {
        var normalizedPattern = (pattern ?? string.Empty).Trim();
        var normalizedProbeKind = (probeKind ?? string.Empty).Trim();
        if (normalizedPattern.Length == 0 || normalizedProbeKind.Length == 0) {
            return false;
        }

        return normalizedPattern.IndexOf(normalizedProbeKind, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static JsonObject BuildSyntheticAdMonitoringProbeArguments(
        string probeKind,
        IReadOnlyList<string> distinctHosts,
        int requiredDistinctHosts) {
        var arguments = new JsonObject(StringComparer.OrdinalIgnoreCase)
            .Add("probe_kind", probeKind);

        var minimumHostCoverage = Math.Max(1, requiredDistinctHosts);
        if (distinctHosts.Count > 0) {
            var selectedHosts = new JsonArray();
            for (var index = 0; index < distinctHosts.Count && index < Math.Max(minimumHostCoverage, distinctHosts.Count); index++) {
                selectedHosts.Add(distinctHosts[index]);
            }

            arguments.Add("targets", selectedHosts);
        } else {
            arguments.Add("discovery_fallback", "current_forest");
        }

        if (string.Equals(probeKind, "ldap", StringComparison.OrdinalIgnoreCase)) {
            arguments.Add("verify_certificate", true);
            arguments.Add("include_global_catalog", true);
            arguments.Add("include_facts", true);
        }

        return arguments;
    }

    private static string NormalizeSyntheticHostTargetCandidate(string value) {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length < 2 || candidate.Length > 128) {
            return string.Empty;
        }

        for (var i = 0; i < candidate.Length; i++) {
            if (char.IsWhiteSpace(candidate[i]) || char.IsControl(candidate[i])) {
                return string.Empty;
            }
        }

        return candidate;
    }

    private static List<string> CollectPlatformBlockedEventLogHosts(
        IReadOnlyList<ToolCall> priorCalls,
        IReadOnlyList<ToolOutput> priorOutputs) {
        var eventLogCallById = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < priorCalls.Count; index++) {
            var call = priorCalls[index];
            if (call is null) {
                continue;
            }

            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0
                || toolName.Length == 0
                || toolName.IndexOf("eventlog_", StringComparison.OrdinalIgnoreCase) < 0) {
                continue;
            }

            eventLogCallById[callId] = call;
        }

        var hosts = new List<string>();
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < priorOutputs.Count; index++) {
            var output = priorOutputs[index];
            if (output is null) {
                continue;
            }

            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0
                || !eventLogCallById.TryGetValue(callId, out var call)
                || !IsPlatformBlockedEventLogOutput(output.Output)) {
                continue;
            }

            if (!TryReadSyntheticAdEventHostTarget(call, out var host)
                || !seenHosts.Add(host)) {
                continue;
            }

            hosts.Add(host);
        }

        return hosts;
    }

    private static bool TryReadSyntheticAdEventHostTarget(ToolCall call, out string host) {
        host = string.Empty;
        if (call?.Arguments is null
            || !ToolHostTargeting.TryReadHostTargetValues(call.Arguments, out var hostTargets)
            || hostTargets.Count == 0) {
            return false;
        }

        var normalizedHost = NormalizeSyntheticHostTargetCandidate(hostTargets[0]);
        if (normalizedHost.Length == 0) {
            return false;
        }

        host = normalizedHost;
        return true;
    }

    private static bool IsPlatformBlockedEventLogOutput(string outputText) {
        var normalized = (outputText ?? string.Empty).Trim();
        if (normalized.Length == 0
            || (!normalized.StartsWith("{", StringComparison.Ordinal) && !normalized.StartsWith("[", StringComparison.Ordinal))) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) {
                return false;
            }

            var root = document.RootElement;
            if (TryReadPlatformBlockedCode(root, "error_code")
                || (root.TryGetProperty("failure", out var failure)
                    && failure.ValueKind == System.Text.Json.JsonValueKind.Object
                    && TryReadPlatformBlockedCode(failure, "code"))) {
                return true;
            }

            return TryReadPlatformBlockedMessage(root, "error")
                   || (root.TryGetProperty("failure", out failure)
                       && failure.ValueKind == System.Text.Json.JsonValueKind.Object
                       && TryReadPlatformBlockedMessage(failure, "message"));
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryReadPlatformBlockedCode(JsonElement node, string propertyName) {
        return node.TryGetProperty(propertyName, out var property)
               && property.ValueKind == System.Text.Json.JsonValueKind.String
               && string.Equals(property.GetString(), "platform_not_supported", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadPlatformBlockedMessage(JsonElement node, string propertyName) {
        return node.TryGetProperty(propertyName, out var property)
               && property.ValueKind == System.Text.Json.JsonValueKind.String
               && (property.GetString() ?? string.Empty).IndexOf("not supported on this platform", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryBuildSyntheticPublicDomainOperationalReplayCalls(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string preferredFamily,
        string rememberedPublicTarget,
        out IReadOnlyList<ToolCall> calls,
        out string reason) {
        calls = Array.Empty<ToolCall>();
        reason = string.Empty;
        if (toolDefinitions is null
            || toolDefinitions.Count == 0
            || LooksLikePureDomainIntentSelection(userRequest)
            || HasMixedDomainIntentSignals(userRequest)
            || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(preferredFamily, out var normalizedFamily)
            || !string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return false;
        }

        var target = ResolvePreferredSyntheticPublicDomainTarget(userRequest, rememberedPublicTarget);
        if (target.Length == 0) {
            return false;
        }

        var queryToolName = ResolveMatchingToolName(toolDefinitions, "dnsclientx_query");
        var summaryToolName = ResolveMatchingToolName(toolDefinitions, "domaindetective_domain_summary");
        if (queryToolName.Length == 0 && summaryToolName.Length == 0) {
            return false;
        }

        var normalizedRequest = ExtractScenarioUserRequestForDomainIntentRouting(userRequest);
        var requestedRecords = ResolveRequestedPublicDnsEvidenceRequests(target, normalizedRequest);
        var requiresDomainSummary = RequestNeedsPublicDomainSummarySupplement(normalizedRequest);
        if (requestedRecords.Count == 0 && (!requiresDomainSummary || summaryToolName.Length == 0)) {
            return false;
        }

        var endpoints = ResolveRequestedDnsEndpoints(userRequest);
        var replayCalls = new List<ToolCall>(requestedRecords.Count + (requiresDomainSummary && summaryToolName.Length > 0 ? 1 : 0));
        if (queryToolName.Length > 0) {
            for (var i = 0; i < requestedRecords.Count; i++) {
                var arguments = new JsonObject(StringComparer.Ordinal)
                    .Add("name", requestedRecords[i].Name)
                    .Add("type", requestedRecords[i].Type)
                    .Add("endpoint", endpoints[i % endpoints.Count]);
                replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(queryToolName, arguments));
            }
        }

        if (requiresDomainSummary && summaryToolName.Length > 0) {
            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                summaryToolName,
                new JsonObject(StringComparer.Ordinal).Add("domain", target)));
        }

        calls = replayCalls;
        reason = $"domain_intent_public_operational_{target}";
        return replayCalls.Count > 0;
    }

    private static bool TryBuildSyntheticPublicProbeReplayCalls(
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string rememberedPublicTarget,
        IReadOnlyList<string>? rememberedPublicHosts,
        bool includeResolverChecks,
        out IReadOnlyList<ToolCall> calls,
        out string reason) {
        calls = Array.Empty<ToolCall>();
        reason = string.Empty;
        var target = NormalizeRememberedDomainTarget(rememberedPublicTarget);
        if (toolDefinitions is null || toolDefinitions.Count == 0 || target.Length == 0) {
            return false;
        }

        var probeTargets = ResolveSyntheticPublicProbeTargets(target, rememberedPublicHosts);
        if (probeTargets.Count == 0) {
            return false;
        }

        var queryToolName = ResolveMatchingToolName(toolDefinitions, "dnsclientx_query");
        var pingToolName = ResolveMatchingToolName(toolDefinitions, "dnsclientx_ping");
        var probeToolName = ResolveMatchingToolName(toolDefinitions, "domaindetective_network_probe");
        var includeAuthoritativeDnsChecks = includeResolverChecks
            && queryToolName.Length > 0
            && HasLikelyAuthoritativeNameserverTargets(probeTargets);
        var replayCalls = new List<ToolCall>(
            probeTargets.Count * (includeResolverChecks ? 3 : 2)
            + (includeAuthoritativeDnsChecks ? MaxSyntheticPublicAuthoritativeEvidenceQueries : 0));

        if (includeAuthoritativeDnsChecks) {
            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                queryToolName,
                new JsonObject(StringComparer.Ordinal)
                    .Add("name", target)
                    .Add("type", "NS")
                    .Add("endpoint", "System")));
            replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                queryToolName,
                new JsonObject(StringComparer.Ordinal)
                    .Add("name", target)
                    .Add("type", "SOA")
                    .Add("endpoint", "System")));
        }

        for (var index = 0; index < probeTargets.Count; index++) {
            var probeTarget = probeTargets[index];
            if (queryToolName.Length > 0 && includeResolverChecks) {
                replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                    queryToolName,
                    new JsonObject(StringComparer.Ordinal)
                        .Add("name", probeTarget)
                        .Add("type", "A")
                        .Add("endpoint", "System")));
            }

            if (pingToolName.Length > 0) {
                replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                    pingToolName,
                    new JsonObject(StringComparer.Ordinal).Add("target", probeTarget)));
            }

            if (probeToolName.Length > 0) {
                replayCalls.Add(BuildSyntheticDomainIntentBootstrapCall(
                    probeToolName,
                    new JsonObject(StringComparer.Ordinal)
                        .Add("host", probeTarget)
                        .Add("run_ping", true)
                        .Add("run_traceroute", false)));
            }
        }

        if (replayCalls.Count == 0) {
            return false;
        }

        calls = replayCalls;
        reason = $"domain_intent_public_probe_{target}";
        return true;
    }

    private static List<string> ResolveSyntheticPublicProbeTargets(
        string target,
        IReadOnlyList<string>? rememberedPublicHosts) {
        var probeTargets = new List<string>(MaxSyntheticPublicProbeTargets);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (rememberedPublicHosts is not null) {
            for (var i = 0; i < rememberedPublicHosts.Count; i++) {
                var candidate = NormalizePublicProbeTargetCandidate(rememberedPublicHosts[i]);
                if (candidate.Length == 0
                    || string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase)
                    || !seen.Add(candidate)) {
                    continue;
                }

                probeTargets.Add(candidate);
                if (probeTargets.Count >= MaxSyntheticPublicProbeTargets) {
                    return probeTargets;
                }
            }
        }

        if (seen.Add(target)) {
            probeTargets.Add(target);
        }

        return probeTargets;
    }

    private static bool HasLikelyAuthoritativeNameserverTargets(IReadOnlyList<string> probeTargets) {
        if (probeTargets is null || probeTargets.Count == 0) {
            return false;
        }

        for (var i = 0; i < probeTargets.Count; i++) {
            if (LooksLikeAuthoritativeNameserverHost(probeTargets[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeAuthoritativeNameserverHost(string value) {
        var candidate = NormalizePublicProbeTargetCandidate(value);
        if (candidate.Length == 0) {
            return false;
        }

        if (Regex.IsMatch(candidate, @"^ns\d+[-.]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
            return true;
        }

        return candidate.IndexOf(".azure-dns.", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ResolvePreferredSyntheticPublicDomainTarget(string userRequest, string rememberedPublicTarget) {
        var domains = ExtractDomainLikeTokens(userRequest);
        if (domains.Count > 0) {
            return domains[0];
        }

        return NormalizeRememberedDomainTarget(rememberedPublicTarget);
    }

    private static string ResolveMatchingToolName(IReadOnlyList<ToolDefinition> toolDefinitions, string expectedName) {
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var toolName = (toolDefinitions[i].Name ?? string.Empty).Trim();
            if (string.Equals(toolName, expectedName, StringComparison.OrdinalIgnoreCase)) {
                return toolName;
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ResolveRequestedDnsEndpoints(string userRequest) {
        var normalized = ExtractScenarioUserRequestForDomainIntentRouting(userRequest);
        var endpoints = new List<string>(2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddEndpoint(string candidate) {
            if (candidate.Length > 0 && seen.Add(candidate)) {
                endpoints.Add(candidate);
            }
        }

        if (normalized.IndexOf("1.1.1.1", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("cloudflare", StringComparison.OrdinalIgnoreCase) >= 0) {
            AddEndpoint("Cloudflare");
        }

        if (normalized.IndexOf("8.8.8.8", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("google", StringComparison.OrdinalIgnoreCase) >= 0) {
            AddEndpoint("Google");
        }

        if (endpoints.Count == 0) {
            endpoints.Add("System");
        }

        return endpoints;
    }

    private static List<(string Name, string Type)> ResolveRequestedPublicDnsEvidenceRequests(string domain, string userRequest) {
        var normalized = userRequest ?? string.Empty;
        var requests = new List<(string Name, string Type)>(5);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRequest(string name, string type) {
            var normalizedName = NormalizeRememberedDomainTarget(name);
            var normalizedType = (type ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedName.Length == 0 || normalizedType.Length == 0) {
                return;
            }

            if (seen.Add(normalizedName + "|" + normalizedType)) {
                requests.Add((normalizedName, normalizedType));
            }
        }

        if (ContainsTechnicalToken(normalized, "A")) {
            AddRequest(domain, "A");
        }

        if (ContainsTechnicalToken(normalized, "AAAA")) {
            AddRequest(domain, "AAAA");
        }

        if (ContainsTechnicalToken(normalized, "MX")) {
            AddRequest(domain, "MX");
        }

        if (ContainsTechnicalToken(normalized, "NS")) {
            AddRequest(domain, "NS");
        }

        if (ContainsTechnicalToken(normalized, "SPF")) {
            AddRequest(domain, "TXT");
        }

        if (ContainsTechnicalToken(normalized, "DMARC")) {
            AddRequest("_dmarc." + domain, "TXT");
        }

        if (requests.Count == 0) {
            AddRequest(domain, "A");
        }

        return requests;
    }

    private static bool RequestNeedsPublicDomainSummarySupplement(string normalizedRequest) {
        return ContainsTechnicalToken(normalizedRequest, "DKIM");
    }

    private static bool RequestNeedsPublicResolverContinuationFromPrompt(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        return normalized.IndexOf("dnsclientx_query", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizePublicProbeTargetCandidate(string value) {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            return string.Empty;
        }

        var separatorIndex = candidate.IndexOf(' ');
        if (separatorIndex > 0
            && int.TryParse(candidate[..separatorIndex], out _)) {
            candidate = candidate[(separatorIndex + 1)..].Trim();
        }

        candidate = candidate.TrimEnd('.');
        if (candidate.Length == 0
            || candidate.Length > 128
            || candidate[0] == '_'
            || candidate.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))
            || Uri.CheckHostName(candidate) == UriHostNameType.Unknown) {
            return string.Empty;
        }

        return candidate;
    }

    private static bool ContainsTechnicalToken(string text, string token) {
        return Regex.IsMatch(
            text ?? string.Empty,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(token)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
