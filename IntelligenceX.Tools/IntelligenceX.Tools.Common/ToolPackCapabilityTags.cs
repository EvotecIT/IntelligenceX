using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared normalized capability tags advertised by tool packs.
/// </summary>
public static class ToolPackCapabilityTags {
    /// <summary>
    /// Pack performs analysis against local data or the local runtime.
    /// </summary>
    public const string LocalAnalysis = "local_analysis";

    /// <summary>
    /// Pack performs analysis against remote hosts, services, or control planes.
    /// </summary>
    public const string RemoteAnalysis = "remote_analysis";

    /// <summary>
    /// Pack can execute commands or actions in the local runtime.
    /// </summary>
    public const string LocalExecution = "local_execution";

    /// <summary>
    /// Pack can execute commands or actions against remote hosts or remote runtimes.
    /// </summary>
    public const string RemoteExecution = "remote_execution";

    /// <summary>
    /// Pack contains mutating tools that can change state.
    /// </summary>
    public const string WriteCapable = "write_capable";

    /// <summary>
    /// Pack contains governed mutating tools that follow explicit lifecycle/write controls.
    /// </summary>
    public const string GovernedWrite = "governed_write";

    /// <summary>
    /// Prefix for pack-declared deferred-work capabilities that should surface in runtime capability snapshots.
    /// </summary>
    public const string DeferredCapabilityPrefix = "deferred_capability:";

    /// <summary>
    /// Pack advertises email-oriented deferred work.
    /// </summary>
    public const string DeferredCapabilityEmail = DeferredCapabilityPrefix + "email";

    /// <summary>
    /// Pack advertises reporting-oriented deferred work.
    /// </summary>
    public const string DeferredCapabilityReporting = DeferredCapabilityPrefix + "reporting";

    /// <summary>
    /// Pack advertises notification-oriented deferred work.
    /// </summary>
    public const string DeferredCapabilityNotification = DeferredCapabilityPrefix + "notification";

    private static readonly string[] PlannerPriorityOrder = {
        GovernedWrite,
        WriteCapable,
        LocalExecution,
        RemoteExecution,
        LocalAnalysis,
        RemoteAnalysis
    };

    /// <summary>
    /// Returns <see langword="true"/> when the pack declares local or remote execution/analysis scope.
    /// </summary>
    public static bool HasExecutionOrAnalysisScope(IReadOnlyList<string>? capabilityTags) {
        if (capabilityTags is not { Count: > 0 }) {
            return false;
        }

        for (var i = 0; i < capabilityTags.Count; i++) {
            if (IsExecutionOrAnalysisScopeTag(capabilityTags[i])) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the pack declares explicit write/governance capability tags.
    /// </summary>
    public static bool HasWriteOrGovernanceTag(IReadOnlyList<string>? capabilityTags) {
        if (capabilityTags is not { Count: > 0 }) {
            return false;
        }

        for (var i = 0; i < capabilityTags.Count; i++) {
            if (IsWriteOrGovernanceTag(capabilityTags[i])) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reorders capability tags so planner-critical locality and governance traits appear first.
    /// </summary>
    public static string[] PrioritizeForPlanner(IReadOnlyList<string>? capabilityTags, int maxCount) {
        if (maxCount <= 0 || capabilityTags is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = capabilityTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) {
            return Array.Empty<string>();
        }

        var prioritized = new List<string>(normalized.Length);
        for (var i = 0; i < PlannerPriorityOrder.Length; i++) {
            var priorityTag = PlannerPriorityOrder[i];
            var match = normalized.FirstOrDefault(tag => string.Equals(tag, priorityTag, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) {
                prioritized.Add(match);
            }
        }

        for (var i = 0; i < normalized.Length; i++) {
            var tag = normalized[i];
            if (!prioritized.Contains(tag, StringComparer.OrdinalIgnoreCase)) {
                prioritized.Add(tag);
            }
        }

        return prioritized
            .Take(maxCount)
            .ToArray();
    }

    /// <summary>
    /// Builds a normalized deferred-work capability tag for the supplied capability identifier.
    /// </summary>
    public static string CreateDeferredCapabilityTag(string? capabilityId) {
        var normalizedCapabilityId = NormalizeDeferredCapabilityId(capabilityId);
        return normalizedCapabilityId.Length == 0
            ? string.Empty
            : DeferredCapabilityPrefix + normalizedCapabilityId;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the capability tag advertises a deferred-work capability id.
    /// </summary>
    public static bool TryGetDeferredCapabilityId(string? capabilityTag, out string capabilityId) {
        capabilityId = string.Empty;
        var normalizedTag = NormalizeDescriptorToken(capabilityTag);
        if (!normalizedTag.StartsWith(DeferredCapabilityPrefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var suffix = normalizedTag[DeferredCapabilityPrefix.Length..];
        suffix = NormalizeDeferredCapabilityId(suffix);
        if (suffix.Length == 0) {
            return false;
        }

        capabilityId = suffix;
        return true;
    }

    private static bool IsExecutionOrAnalysisScopeTag(string? capabilityTag) {
        return string.Equals(capabilityTag, LocalAnalysis, StringComparison.OrdinalIgnoreCase)
               || string.Equals(capabilityTag, RemoteAnalysis, StringComparison.OrdinalIgnoreCase)
               || string.Equals(capabilityTag, LocalExecution, StringComparison.OrdinalIgnoreCase)
               || string.Equals(capabilityTag, RemoteExecution, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWriteOrGovernanceTag(string? capabilityTag) {
        return string.Equals(capabilityTag, WriteCapable, StringComparison.OrdinalIgnoreCase)
               || string.Equals(capabilityTag, GovernedWrite, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDescriptorToken(string? capabilityTag) {
        return (capabilityTag ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeDeferredCapabilityId(string? capabilityId) {
        var normalized = NormalizeDescriptorToken(capabilityId)
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
        while (normalized.Contains("__", StringComparison.Ordinal)) {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized.Trim('_');
    }
}
