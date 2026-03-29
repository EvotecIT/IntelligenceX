using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared builders for structured startup bootstrap warning summaries emitted by the service.
/// </summary>
public static class StartupBootstrapWarningBuilder {
    /// <summary>
    /// Builds the summary warning for overall tooling bootstrap timings.
    /// </summary>
    public static string BuildTimingSummary(
        string total,
        string policy,
        string options,
        string packs,
        string register,
        string finalize,
        string registry,
        int tools,
        int packsLoaded,
        int packsDisabled,
        int pluginRoots) {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[startup] tooling bootstrap timings total={NormalizeToken(total)} " +
            $"policy={NormalizeToken(policy)} " +
            $"options={NormalizeToken(options)} " +
            $"descriptorDiscovery={NormalizeToken(packs)} " +
            $"packActivation={NormalizeToken(register)} " +
            $"activationFinalize={NormalizeToken(finalize)} " +
            $"registry={NormalizeToken(registry)} " +
            $"tools={Math.Max(0, tools)} packsLoaded={Math.Max(0, packsLoaded)} packsDisabled={Math.Max(0, packsDisabled)} pluginRoots={Math.Max(0, pluginRoots)}.");
    }

    /// <summary>
    /// Builds the warning emitted when tooling bootstrap reuses an in-memory cache snapshot.
    /// </summary>
    public static string BuildCacheHitSummary(long elapsedMs, int tools, int packsLoaded) {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[startup] tooling bootstrap cache hit elapsed={Math.Max(1, elapsedMs)}ms tools={Math.Max(0, tools)} packsLoaded={Math.Max(0, packsLoaded)}.");
    }

    /// <summary>
    /// Builds the warning emitted when a persisted preview catalog is restored while the runtime rebuild continues.
    /// </summary>
    public static string BuildPersistedPreviewRestoredSummary() {
        return "[startup] tooling bootstrap preview restored from persisted cache while runtime rebuild continues.";
    }

    /// <summary>
    /// Builds the warning emitted when a persisted preview payload is ignored and the runtime must rebuild live tooling metadata.
    /// </summary>
    public static string BuildPersistedPreviewIgnoredSummary(
        string reason,
        int? expectedSchemaVersion = null,
        int? actualSchemaVersion = null,
        string? detail = null) {
        var warning = string.Create(
            CultureInfo.InvariantCulture,
            $"[startup] tooling bootstrap persisted preview ignored reason={NormalizeToken(reason)}");

        if (expectedSchemaVersion.HasValue) {
            warning += string.Create(
                CultureInfo.InvariantCulture,
                $" expectedSchemaVersion={Math.Max(0, expectedSchemaVersion.Value)}");
        }

        if (actualSchemaVersion.HasValue) {
            warning += string.Create(
                CultureInfo.InvariantCulture,
                $" actualSchemaVersion={Math.Max(0, actualSchemaVersion.Value)}");
        }

        var normalizedDetail = (detail ?? string.Empty).Trim();
        if (normalizedDetail.Length > 0) {
            warning += string.Create(
                CultureInfo.InvariantCulture,
                $" detail={NormalizeToken(normalizedDetail)}");
        }

        return warning + ".";
    }

    /// <summary>
    /// Builds the summary warning for plugin load progress.
    /// </summary>
    public static string BuildPluginLoadProgressSummary(int processed, int total, int beginCount, int endCount) {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[startup] plugin load progress: processed {Math.Max(0, processed)}/{Math.Max(0, total)} plugin folders (begin={Math.Max(0, beginCount)}, end={Math.Max(0, endCount)}).");
    }

    /// <summary>
    /// Builds the summary warning for pack load progress.
    /// </summary>
    public static string BuildPackLoadProgressSummary(int processed, int total, int beginCount, int endCount) {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[startup] pack load progress: processed {Math.Max(0, processed)}/{Math.Max(0, total)} bootstrap steps (begin={Math.Max(0, beginCount)}, end={Math.Max(0, endCount)}).");
    }

    /// <summary>
    /// Builds the summary warning for pack registration progress.
    /// </summary>
    public static string BuildPackRegistrationProgressSummary(int processed, int total, int beginCount, int endCount) {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[startup] pack registration progress: processed {Math.Max(0, processed)}/{Math.Max(0, total)} packs (begin={Math.Max(0, beginCount)}, end={Math.Max(0, endCount)}).");
    }

    /// <summary>
    /// Builds the summary warning for slow plugin loads.
    /// </summary>
    public static string BuildSlowPluginLoadsTop(int topCount, int totalCount, IEnumerable<string> segments) {
        return BuildTopSummary("[startup] slow plugin loads top", topCount, totalCount, segments);
    }

    /// <summary>
    /// Builds the omission warning for truncated slow plugin load summaries.
    /// </summary>
    public static string BuildAdditionalSlowPluginsOmitted(int omittedCount) {
        return BuildOmittedSummary("[startup] additional slow plugins omitted", omittedCount);
    }

    /// <summary>
    /// Builds the summary warning for slow pack loads.
    /// </summary>
    public static string BuildSlowPackLoadsTop(int topCount, int totalCount, IEnumerable<string> segments) {
        return BuildTopSummary("[startup] slow pack loads top", topCount, totalCount, segments);
    }

    /// <summary>
    /// Builds the omission warning for truncated slow pack load summaries.
    /// </summary>
    public static string BuildAdditionalSlowPacksOmitted(int omittedCount) {
        return BuildOmittedSummary("[startup] additional slow packs omitted", omittedCount);
    }

    /// <summary>
    /// Builds the summary warning for slow pack registrations.
    /// </summary>
    public static string BuildSlowPackRegistrationsTop(int topCount, int totalCount, IEnumerable<string> segments) {
        return BuildTopSummary("[startup] slow pack registrations top", topCount, totalCount, segments);
    }

    /// <summary>
    /// Builds the omission warning for truncated slow pack registration summaries.
    /// </summary>
    public static string BuildAdditionalSlowPackRegistrationsOmitted(int omittedCount) {
        return BuildOmittedSummary("[startup] additional slow pack registrations omitted", omittedCount);
    }

    private static string BuildTopSummary(string prefix, int topCount, int totalCount, IEnumerable<string> segments) {
        ArgumentNullException.ThrowIfNull(segments);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix} {Math.Max(0, topCount)}/{Math.Max(0, totalCount)}: {string.Join("; ", segments)}");
    }

    private static string BuildOmittedSummary(string prefix, int omittedCount) {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}: {Math.Max(0, omittedCount)}.");
    }

    private static string NormalizeToken(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? "unknown" : normalized;
    }
}
