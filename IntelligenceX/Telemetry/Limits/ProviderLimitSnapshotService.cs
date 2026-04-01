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
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>
/// Fetches live provider limits using reusable provider-native APIs.
/// </summary>
public sealed partial class ProviderLimitSnapshotService {
    private const int MaxConcurrentProviderFetches = 2;
    private readonly Func<string, CancellationToken, Task<ProviderLimitSnapshot>>? _fetchOverride;

    internal ProviderLimitSnapshotService(
        Func<string, CancellationToken, Task<ProviderLimitSnapshot>> fetchOverride) {
        _fetchOverride = fetchOverride ?? throw new ArgumentNullException(nameof(fetchOverride));
    }

    /// <summary>
    /// Initializes the provider limit snapshot service.
    /// </summary>
    public ProviderLimitSnapshotService() {
    }

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

        using var gate = new SemaphoreSlim(Math.Min(MaxConcurrentProviderFetches, keys.Length));
        var tasks = keys
            .Select(async key => {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try {
                    return await FetchSingleResilientAsync(key, cancellationToken).ConfigureAwait(false);
                } finally {
                    gate.Release();
                }
            })
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
        return FetchSingleCoreAsync(normalized, cancellationToken);
    }

    private async Task<ProviderLimitSnapshot> FetchSingleResilientAsync(string requestedProviderId, CancellationToken cancellationToken) {
        try {
            return await FetchSingleCoreAsync(requestedProviderId, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            if (cancellationToken.IsCancellationRequested) {
                throw;
            }

            return BuildUnavailableSnapshot(
                requestedProviderId,
                "Live limits request was canceled before completion.");
        } catch (Exception ex) {
            return BuildUnavailableSnapshot(requestedProviderId, ex.Message);
        }
    }

    private Task<ProviderLimitSnapshot> FetchSingleCoreAsync(string requestedProviderId, CancellationToken cancellationToken) {
        if (_fetchOverride is not null) {
            return _fetchOverride(requestedProviderId, cancellationToken);
        }

        return FetchSingleAsync(requestedProviderId, cancellationToken);
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
        options.AuthAccountId = TryResolveCurrentCodexAccountId(options.CodexHome);
        var snapshot = await FetchPreferredOpenAiSnapshotAsync(options, cancellationToken).ConfigureAwait(false);
        var primary = BuildOpenAiLimitPresentation(snapshot);
        var selectedAccountKey = BuildOpenAiAccountKey(snapshot.AccountId, snapshot.Email, accessToken: null);
        var accountSnapshots = await FetchCodexAccountSnapshotsAsync(options, snapshot, selectedAccountKey, cancellationToken).ConfigureAwait(false);

        return new ProviderLimitSnapshot(
            requestedProviderId,
            UsageTelemetryProviderCatalog.ResolveDisplayTitle("codex"),
            "OpenAI usage API",
            primary.PlanLabel,
            primary.AccountLabel,
            primary.Windows,
            primary.Summary,
            primary.DetailMessage,
            DateTimeOffset.UtcNow,
            accountSnapshots);
    }

    private static void AddOpenAiStatusWindows(
        ICollection<ProviderLimitWindow> windows,
        string keyPrefix,
        string scopeLabel,
        ChatGptRateLimitStatus? status) {
        if (status is null) {
            return;
        }

        AddOpenAiWindow(
            windows,
            keyPrefix + "-primary",
            status.PrimaryWindow,
            ComposeOpenAiWindowLabel(scopeLabel, DescribeOpenAiWindow(status.PrimaryWindow?.LimitWindowSeconds, "5-hour")));
        AddOpenAiWindow(
            windows,
            keyPrefix + "-secondary",
            status.SecondaryWindow,
            ComposeOpenAiWindowLabel(scopeLabel, DescribeOpenAiWindow(status.SecondaryWindow?.LimitWindowSeconds, "Weekly")));
    }

    private static void AddOpenAiAdditionalRateLimitWindows(
        ICollection<ProviderLimitWindow> windows,
        IReadOnlyList<ChatGptNamedRateLimit> additionalRateLimits) {
        if (additionalRateLimits.Count == 0) {
            return;
        }

        for (var i = 0; i < additionalRateLimits.Count; i++) {
            var additionalRateLimit = additionalRateLimits[i];
            var scopeLabel = DescribeOpenAiAdditionalRateLimit(additionalRateLimit);
            AddOpenAiStatusWindows(windows, "additional-" + i.ToString(CultureInfo.InvariantCulture), scopeLabel, additionalRateLimit.RateLimit);
        }
    }

    private static async Task<IReadOnlyList<ProviderLimitAccountSnapshot>> FetchCodexAccountSnapshotsAsync(
        OpenAINativeOptions options,
        ChatGptUsageSnapshot selectedSnapshot,
        string? selectedAccountKey,
        CancellationToken cancellationToken) {
        var bundles = await ListOpenAiBundlesAsync(options.AuthStore, cancellationToken).ConfigureAwait(false);
        if (bundles.Count == 0) {
            return Array.Empty<ProviderLimitAccountSnapshot>();
        }

        var results = new List<ProviderLimitAccountSnapshot>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var client = new ChatGptUsageClient();

        foreach (var bundle in bundles) {
            cancellationToken.ThrowIfCancellationRequested();

            var accountId = NormalizeOptional(bundle.AccountId) ?? NormalizeOptional(JwtDecoder.TryGetAccountId(bundle.AccessToken));
            var email = NormalizeOptional(JwtDecoder.TryGetEmail(bundle.IdToken ?? bundle.AccessToken));
            var detectedAccountLabel = email ?? accountId;
            var key = BuildOpenAiAccountKey(accountId, email, bundle.AccessToken);
            if (!seenKeys.Add(key)) {
                continue;
            }

            ChatGptUsageSnapshot snapshot;
            if (selectedAccountKey is not null && string.Equals(selectedAccountKey, key, StringComparison.OrdinalIgnoreCase)) {
                snapshot = selectedSnapshot;
            } else {
                try {
                    snapshot = await client.GetUsageAsync(
                            options.ChatGptApiBaseUrl,
                            bundle.AccessToken,
                            accountId,
                            options.UserAgent,
                            cancellationToken)
                        .ConfigureAwait(false);
                } catch (Exception ex) {
                    results.Add(new ProviderLimitAccountSnapshot(
                        accountId: accountId,
                        accountLabel: detectedAccountLabel,
                        planLabel: null,
                        windows: Array.Empty<ProviderLimitWindow>(),
                        summary: "Detected locally, but live limits are unavailable.",
                        detailMessage: BuildUnavailableOpenAiAccountDetail(bundle, ex),
                        retrievedAtUtc: DateTimeOffset.UtcNow,
                        isSelected: selectedAccountKey is not null && string.Equals(selectedAccountKey, key, StringComparison.OrdinalIgnoreCase)));
                    continue;
                }
            }

            var presentation = BuildOpenAiLimitPresentation(snapshot);
            results.Add(new ProviderLimitAccountSnapshot(
                accountId: NormalizeOptional(snapshot.AccountId) ?? accountId,
                accountLabel: presentation.AccountLabel,
                planLabel: presentation.PlanLabel,
                windows: presentation.Windows,
                summary: presentation.Summary,
                detailMessage: presentation.DetailMessage,
                retrievedAtUtc: DateTimeOffset.UtcNow,
                isSelected: selectedAccountKey is not null && string.Equals(selectedAccountKey, key, StringComparison.OrdinalIgnoreCase)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<AuthBundle>> ListOpenAiBundlesAsync(IAuthBundleStore authStore, CancellationToken cancellationToken) {
        var providers = new[] { OpenAICodexDefaults.Provider, "openai", "chatgpt" };
        var bundles = new List<AuthBundle>();
        foreach (var provider in providers) {
            var entries = await authStore.ListAsync(provider, cancellationToken).ConfigureAwait(false);
            if (entries.Count > 0) {
                bundles.AddRange(entries);
            }
        }

        return bundles;
    }

    private static OpenAiLimitPresentation BuildOpenAiLimitPresentation(ChatGptUsageSnapshot snapshot) {
        var windows = new List<ProviderLimitWindow>();
        AddOpenAiStatusWindows(windows, "global", "Global", snapshot.RateLimit);
        AddOpenAiAdditionalRateLimitWindows(windows, snapshot.AdditionalRateLimits);

        var summaryParts = new List<string>();
        if (snapshot.Credits is not null) {
            if (snapshot.Credits.Unlimited) {
                summaryParts.Add("Unlimited credits");
            } else if (snapshot.Credits.Balance.HasValue) {
                summaryParts.Add("Credits $" + snapshot.Credits.Balance.Value.ToString("F2", CultureInfo.InvariantCulture));
            }

            var localRange = FormatApproximateMessageRange(snapshot.Credits.ApproxLocalMessages);
            if (localRange is not null) {
                summaryParts.Add("Local msgs " + localRange);
            }

            var cloudRange = FormatApproximateMessageRange(snapshot.Credits.ApproxCloudMessages);
            if (cloudRange is not null) {
                summaryParts.Add("API msgs " + cloudRange);
            }
        }

        if (snapshot.RateLimit?.LimitReached == true) {
            summaryParts.Add("Active request window is exhausted");
        }

        return new OpenAiLimitPresentation(
            NormalizeOptional(snapshot.PlanType),
            NormalizeOptional(snapshot.Email) ?? NormalizeOptional(snapshot.AccountId),
            windows,
            summaryParts.Count == 0 ? null : string.Join(" • ", summaryParts),
            windows.Count == 0 ? "No live rate-limit windows were returned by OpenAI." : null);
    }

    private static async Task<ChatGptUsageSnapshot> FetchPreferredOpenAiSnapshotAsync(
        OpenAINativeOptions options,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(options.AuthAccountId)) {
            try {
                using var preferredUsageService = new ChatGptUsageService(options);
                return await preferredUsageService.GetUsageSnapshotAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch {
                options.AuthAccountId = null;
            }
        }

        using var fallbackUsageService = new ChatGptUsageService(options);
        return await fallbackUsageService.GetUsageSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? TryResolveCurrentCodexAccountId(string? codexHome) {
        var authPath = CodexAuthStore.ResolveAuthPath(codexHome);
        return NormalizeOptional(CodexAuthStore.TryReadProfile(authPath)?.AccountId);
    }

    private static string BuildUnavailableOpenAiAccountDetail(AuthBundle bundle, Exception ex) {
        if (bundle.ExpiresAt.HasValue && bundle.ExpiresAt.Value <= DateTimeOffset.UtcNow) {
            return "Local login expired on "
                   + bundle.ExpiresAt.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture)
                   + ". Reauthenticate this account to load live limits.";
        }

        var message = NormalizeOptional(ex.Message);
        if (!string.IsNullOrWhiteSpace(message)) {
            return "Live limits request failed: " + message;
        }

        return "Live limits request failed for this account.";
    }

    private static string BuildOpenAiAccountKey(string? accountId, string? email, string? accessToken) {
        return NormalizeOptional(accountId)
               ?? NormalizeOptional(email)
               ?? ("token:" + UsageTelemetryIdentity.ComputeStableHash(NormalizeOptional(accessToken) ?? "unknown", 12));
    }

    private static void AddOpenAiWindow(
        ICollection<ProviderLimitWindow> windows,
        string key,
        ChatGptRateLimitWindow? window,
        string label) {
        if (window is null) {
            return;
        }

        var resetsAt = ResolveResetAt(window.ResetAtUnixSeconds, window.ResetAfterSeconds);
        windows.Add(new ProviderLimitWindow(
            key,
            label,
            NormalizePercent(window.UsedPercent),
            resetsAt,
            windowDuration: window.LimitWindowSeconds is > 0
                ? TimeSpan.FromSeconds(window.LimitWindowSeconds.Value)
                : null));
    }

    private static string DescribeOpenAiWindow(long? limitWindowSeconds, string fallbackLabel) {
        if (!limitWindowSeconds.HasValue || limitWindowSeconds.Value <= 0) {
            return fallbackLabel;
        }

        var seconds = limitWindowSeconds.Value;
        if (seconds >= 6 * 24 * 60 * 60) {
            return "weekly";
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

    private static string ComposeOpenAiWindowLabel(string scopeLabel, string windowLabel) {
        var normalizedScope = NormalizeOptional(scopeLabel);
        var normalizedWindow = NormalizeOptional(windowLabel);
        if (normalizedScope is null) {
            return normalizedWindow ?? "Rate limit";
        }
        if (normalizedWindow is null) {
            return normalizedScope;
        }

        if (normalizedWindow.Equals("weekly", StringComparison.OrdinalIgnoreCase)) {
            normalizedWindow = "Weekly";
        }

        return normalizedScope + " " + normalizedWindow;
    }

    private static string DescribeOpenAiAdditionalRateLimit(ChatGptNamedRateLimit additionalRateLimit) {
        var limitName = NormalizeOptional(additionalRateLimit.LimitName);
        if (limitName is not null) {
            if (limitName.IndexOf("spark", StringComparison.OrdinalIgnoreCase) >= 0) {
                return "Spark";
            }

            return limitName;
        }

        var meteredFeature = NormalizeOptional(additionalRateLimit.MeteredFeature);
        if (meteredFeature is not null &&
            meteredFeature.IndexOf("bengalfox", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "Spark";
        }

        return "Additional";
    }

    private static string? FormatApproximateMessageRange(int[]? values) {
        if (values is null || values.Length == 0) {
            return null;
        }

        var normalized = values.Select(static value => Math.Max(0, value)).ToArray();
        if (normalized.Length == 1) {
            return normalized[0].ToString(CultureInfo.InvariantCulture);
        }

        return normalized[0].ToString(CultureInfo.InvariantCulture)
               + "-"
               + normalized[normalized.Length - 1].ToString(CultureInfo.InvariantCulture);
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

    private sealed record OpenAiLimitPresentation(
        string? PlanLabel,
        string? AccountLabel,
        IReadOnlyList<ProviderLimitWindow> Windows,
        string? Summary,
        string? DetailMessage);

}
