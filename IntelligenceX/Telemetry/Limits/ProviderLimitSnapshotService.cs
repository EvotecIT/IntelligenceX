using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>
/// Fetches live provider limits using reusable provider-native APIs.
/// </summary>
public sealed partial class ProviderLimitSnapshotService {

    /// <summary>
    /// Fetches live limits for the requested providers.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ProviderLimitSnapshot>> FetchAsync(
        IEnumerable<string> providerIds,
        CancellationToken cancellationToken = default) {
        if (providerIds is null) {
            throw new ArgumentNullException(nameof(providerIds));
        }

        var keys = providerIds
            .Select(NormalizeOptional)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0) {
            return new Dictionary<string, ProviderLimitSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var tasks = keys
            .Select(key => FetchSingleAsync(key, cancellationToken))
            .ToArray();
        var snapshots = await Task.WhenAll(tasks).ConfigureAwait(false);

        var map = new Dictionary<string, ProviderLimitSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots) {
            map[snapshot.ProviderId] = snapshot;
        }
        return map;
    }

    /// <summary>
    /// Fetches live limits for a single provider.
    /// </summary>
    public Task<ProviderLimitSnapshot> FetchAsync(string providerId, CancellationToken cancellationToken = default) {
        var normalized = NormalizeOptional(providerId)
                         ?? throw new ArgumentException("Provider id cannot be null or whitespace.", nameof(providerId));
        return FetchSingleAsync(normalized, cancellationToken);
    }

    private async Task<ProviderLimitSnapshot> FetchSingleAsync(string requestedProviderId, CancellationToken cancellationToken) {
        var canonicalProviderId = UsageTelemetryProviderCatalog.ResolveCanonicalProviderId(requestedProviderId) ?? requestedProviderId;
        try {
            if (string.Equals(canonicalProviderId, "codex", StringComparison.OrdinalIgnoreCase)
                || string.Equals(canonicalProviderId, "chatgpt", StringComparison.OrdinalIgnoreCase)) {
                return await FetchCodexAsync(requestedProviderId, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(canonicalProviderId, "copilot", StringComparison.OrdinalIgnoreCase)) {
                return await FetchCopilotAsync(requestedProviderId, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(canonicalProviderId, "claude", StringComparison.OrdinalIgnoreCase)) {
                return await FetchClaudeAsync(requestedProviderId, cancellationToken).ConfigureAwait(false);
            }

            return BuildUnavailableSnapshot(
                requestedProviderId,
                "Live limits are not available for this provider.");
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return BuildUnavailableSnapshot(requestedProviderId, ex.Message);
        }
    }

    private static async Task<ProviderLimitSnapshot> FetchCodexAsync(string requestedProviderId, CancellationToken cancellationToken) {
        var options = new OpenAINativeOptions {
            UserAgent = "IntelligenceX/0.1.0"
        };

        using var usageService = new ChatGptUsageService(options);
        var snapshot = await usageService.GetUsageSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var windows = new List<ProviderLimitWindow>();
        AddOpenAiWindow(windows, "primary", snapshot.RateLimit?.PrimaryWindow, "5-hour");
        AddOpenAiWindow(windows, "secondary", snapshot.RateLimit?.SecondaryWindow, "Weekly");
        AddOpenAiWindow(windows, "code-review", snapshot.CodeReviewRateLimit?.PrimaryWindow, "Code review");

        var summaryParts = new List<string>();
        if (snapshot.Credits is not null) {
            if (snapshot.Credits.Unlimited) {
                summaryParts.Add("Unlimited credits");
            } else if (snapshot.Credits.Balance.HasValue) {
                summaryParts.Add("Credits $" + snapshot.Credits.Balance.Value.ToString("F2", CultureInfo.InvariantCulture));
            }
        }
        if (snapshot.RateLimit?.LimitReached == true) {
            summaryParts.Add("Active request window is exhausted");
        }

        return new ProviderLimitSnapshot(
            requestedProviderId,
            UsageTelemetryProviderCatalog.ResolveDisplayTitle("codex"),
            "OpenAI usage API",
            NormalizeOptional(snapshot.PlanType),
            NormalizeOptional(snapshot.Email) ?? NormalizeOptional(snapshot.AccountId),
            windows,
            summaryParts.Count == 0 ? null : string.Join(" • ", summaryParts),
            windows.Count == 0 ? "No live rate-limit windows were returned by OpenAI." : null,
            DateTimeOffset.UtcNow);
    }

    private static void AddOpenAiWindow(
        ICollection<ProviderLimitWindow> windows,
        string key,
        ChatGptRateLimitWindow? window,
        string fallbackLabel) {
        if (window is null) {
            return;
        }

        var label = DescribeOpenAiWindow(window.LimitWindowSeconds, fallbackLabel);
        var resetsAt = ResolveResetAt(window.ResetAtUnixSeconds, window.ResetAfterSeconds);
        windows.Add(new ProviderLimitWindow(
            key,
            label,
            NormalizePercent(window.UsedPercent),
            resetsAt));
    }

    private static string DescribeOpenAiWindow(long? limitWindowSeconds, string fallbackLabel) {
        if (!limitWindowSeconds.HasValue || limitWindowSeconds.Value <= 0) {
            return fallbackLabel;
        }

        var seconds = limitWindowSeconds.Value;
        if (seconds >= 6 * 24 * 60 * 60) {
            return "Weekly";
        }
        if (seconds >= 4 * 60 * 60 && seconds <= 6 * 60 * 60) {
            return "5-hour";
        }
        if (seconds >= 60 * 60) {
            return (seconds / 3600L).ToString(CultureInfo.InvariantCulture) + "-hour";
        }
        if (seconds >= 60) {
            return (seconds / 60L).ToString(CultureInfo.InvariantCulture) + "-minute";
        }
        return fallbackLabel;
    }

    private static DateTimeOffset? ResolveResetAt(long? resetAtUnixSeconds, long? resetAfterSeconds) {
        if (resetAtUnixSeconds.HasValue && resetAtUnixSeconds.Value > 0) {
            try {
                return DateTimeOffset.FromUnixTimeSeconds(resetAtUnixSeconds.Value);
            } catch {
                // Ignore bad server values and fall back to reset-after.
            }
        }

        if (resetAfterSeconds.HasValue && resetAfterSeconds.Value > 0) {
            return DateTimeOffset.UtcNow.AddSeconds(resetAfterSeconds.Value);
        }

        return null;
    }

    private static async Task<ProviderLimitSnapshot> FetchCopilotAsync(string requestedProviderId, CancellationToken cancellationToken) {
        var token = GitHubDashboardService.ResolveTokenFromEnvironment();
        if (string.IsNullOrWhiteSpace(token)) {
            return BuildUnavailableSnapshot(
                requestedProviderId,
                "Set INTELLIGENCEX_GITHUB_TOKEN, GITHUB_TOKEN, or GH_TOKEN to fetch Copilot live limits.",
                "GitHub Copilot API");
        }

        using var client = new CopilotQuotaSnapshotClient();
        var snapshot = await client.FetchAsync(token!, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) {
            return BuildUnavailableSnapshot(
                requestedProviderId,
                "GitHub Copilot did not return usable quota data.",
                "GitHub Copilot API");
        }

        var windows = new List<ProviderLimitWindow>();
        AddCopilotWindow(windows, "premium", "Premium", snapshot.PremiumInteractions, snapshot.QuotaResetDate);
        AddCopilotWindow(windows, "chat", "Chat", snapshot.Chat, snapshot.QuotaResetDate);

        return new ProviderLimitSnapshot(
            requestedProviderId,
            UsageTelemetryProviderCatalog.ResolveDisplayTitle("copilot"),
            "GitHub Copilot API",
            FormatCopilotPlan(snapshot.Plan),
            null,
            windows,
            snapshot.QuotaResetDate.HasValue
                ? "Quota resets " + FormatAbsoluteReset(snapshot.QuotaResetDate.Value)
                : null,
            windows.Count == 0 ? "GitHub Copilot returned a quota payload without usable windows." : null,
            DateTimeOffset.UtcNow);
    }

    private static void AddCopilotWindow(
        ICollection<ProviderLimitWindow> windows,
        string key,
        string label,
        CopilotQuotaWindow? quota,
        DateTimeOffset? resetsAt) {
        if (quota is null) {
            return;
        }

        var detail = quota.Entitlement > 0d
            ? quota.Remaining.ToString("0", CultureInfo.InvariantCulture)
              + "/"
              + quota.Entitlement.ToString("0", CultureInfo.InvariantCulture)
              + " remaining"
            : null;

        windows.Add(new ProviderLimitWindow(
            key,
            label,
            NormalizePercent(quota.UsedPercent),
            resetsAt,
            detail));
    }

    private static ProviderLimitSnapshot BuildUnavailableSnapshot(
        string requestedProviderId,
        string detailMessage,
        string? sourceLabel = null) {
        var displayName = UsageTelemetryProviderCatalog.ResolveDisplayTitle(requestedProviderId);
        return new ProviderLimitSnapshot(
            requestedProviderId,
            displayName,
            sourceLabel ?? (displayName + " limits"),
            null,
            null,
            Array.Empty<ProviderLimitWindow>(),
            null,
            NormalizeOptional(detailMessage),
            DateTimeOffset.UtcNow);
    }

    private static string? FormatCopilotPlan(string? plan) {
        var normalized = NormalizeOptional(plan);
        if (normalized is null) {
            return null;
        }

        return normalized.ToLowerInvariant() switch {
            "free" => "GitHub Copilot Free",
            "pro" => "GitHub Copilot Pro",
            "business" => "GitHub Copilot Business",
            "enterprise" => "GitHub Copilot Enterprise",
            _ => "GitHub Copilot " + normalized
        };
    }

    private static string FormatAbsoluteReset(DateTimeOffset resetsAt) {
        return resetsAt.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
    }

    private static double? NormalizePercent(double? value) {
        if (!value.HasValue) {
            return null;
        }

        return Math.Min(100d, Math.Max(0d, value.Value));
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property) {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property)) {
            return true;
        }

        property = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var property)) {
            return null;
        }

        return property.ValueKind switch {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static bool? ReadBoolean(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var property)) {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True) {
            return true;
        }
        if (property.ValueKind == JsonValueKind.False) {
            return false;
        }
        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out var parsed)) {
            return parsed;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var property)) {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number)) {
            return number;
        }
        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) {
        var normalized = NormalizeOptional(value);
        if (normalized is null) {
            return null;
        }

        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            return parsed;
        }

        return null;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

}
