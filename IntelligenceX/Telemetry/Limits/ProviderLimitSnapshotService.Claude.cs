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
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Limits;

public sealed partial class ProviderLimitSnapshotService {
    private static readonly Uri ClaudeOAuthUsageUri = new("https://api.anthropic.com/api/oauth/usage", UriKind.Absolute);
    private static readonly Uri ClaudeOrganizationsUri = new("https://claude.ai/api/organizations", UriKind.Absolute);
    private static readonly Uri ClaudeAccountUri = new("https://claude.ai/api/account", UriKind.Absolute);

    private static async Task<ProviderLimitSnapshot> FetchClaudeAsync(string requestedProviderId, CancellationToken cancellationToken) {
        var credentials = TryLoadClaudeCredentials();
        var oauthAccessToken = NormalizeOptional(credentials?.AccessToken);
        var hasProfileScope = credentials?.Scopes.Any(scope =>
            string.Equals(scope, "user:profile", StringComparison.OrdinalIgnoreCase)) == true;

        if (!string.IsNullOrWhiteSpace(oauthAccessToken) && hasProfileScope) {
            using var httpClient = CreateHttpClient();
            var oauthSnapshot = await FetchClaudeOAuthAsync(httpClient, oauthAccessToken!, credentials, cancellationToken).ConfigureAwait(false);
            if (oauthSnapshot is not null) {
                return new ProviderLimitSnapshot(
                    requestedProviderId,
                    UsageTelemetryProviderCatalog.ResolveDisplayTitle("claude"),
                    "Claude OAuth usage API",
                    oauthSnapshot.PlanLabel,
                    null,
                    oauthSnapshot.Windows,
                    oauthSnapshot.Summary,
                    oauthSnapshot.Windows.Count == 0 ? "Claude OAuth returned no live rate-limit windows." : null,
                    DateTimeOffset.UtcNow);
            }
        }

        var sessionKey = ResolveClaudeSessionKeyFromEnvironment();
        if (!string.IsNullOrWhiteSpace(sessionKey)) {
            using var httpClient = CreateHttpClient();
            var webSnapshot = await FetchClaudeWebAsync(httpClient, sessionKey!, cancellationToken).ConfigureAwait(false);
            return new ProviderLimitSnapshot(
                requestedProviderId,
                UsageTelemetryProviderCatalog.ResolveDisplayTitle("claude"),
                "Claude web usage API",
                webSnapshot.PlanLabel,
                webSnapshot.AccountLabel,
                webSnapshot.Windows,
                webSnapshot.Summary,
                webSnapshot.Windows.Count == 0 ? "Claude web usage did not return live limit windows." : null,
                DateTimeOffset.UtcNow);
        }

        var message = hasProfileScope
            ? "Claude credentials were found, but live usage was not available. If OAuth fails, set INTELLIGENCEX_CLAUDE_SESSION_KEY (or CLAUDE_SESSION_KEY) with a claude.ai session key."
            : "Claude live limits need OAuth credentials with user:profile scope, or INTELLIGENCEX_CLAUDE_SESSION_KEY / CLAUDE_SESSION_KEY for the claude.ai web usage API.";

        return BuildUnavailableSnapshot(
            requestedProviderId,
            message,
            "Claude usage API");
    }

    private static async Task<ClaudeProviderSnapshot?> FetchClaudeOAuthAsync(
        HttpClient httpClient,
        string accessToken,
        ClaudeCredentials? credentials,
        CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, ClaudeOAuthUsageUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) {
            throw new InvalidOperationException("Claude OAuth rejected the live-usage request. Run `claude` to refresh authentication or use a web session key.");
        }
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                "Claude OAuth usage request failed with HTTP "
                + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                + ".");
        }

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var windows = new List<ProviderLimitWindow>();
        AddClaudeWindow(windows, "session", "Session", ReadClaudeUsageWindow(root, "five_hour"));
        AddClaudeWindow(windows, "weekly", "Weekly", ReadClaudeUsageWindow(root, "seven_day"));
        AddClaudeWindow(windows, "sonnet", "Sonnet weekly", ReadClaudeUsageWindow(root, "seven_day_sonnet"));
        AddClaudeWindow(windows, "opus", "Opus weekly", ReadClaudeUsageWindow(root, "seven_day_opus"));

        var extraUsageSummary = FormatClaudeExtraUsage(root);
        return new ClaudeProviderSnapshot(
            InferClaudePlan(credentials?.RateLimitTier, credentials?.SubscriptionType),
            null,
            windows,
            extraUsageSummary);
    }

    private static async Task<ClaudeProviderSnapshot> FetchClaudeWebAsync(
        HttpClient httpClient,
        string sessionKey,
        CancellationToken cancellationToken) {
        var organization = await FetchClaudeOrganizationAsync(httpClient, sessionKey, cancellationToken).ConfigureAwait(false);
        if (organization is null) {
            throw new InvalidOperationException("Claude did not return any organizations for the provided session.");
        }

        var usageUri = new Uri("https://claude.ai/api/organizations/" + Uri.EscapeDataString(organization.Id) + "/usage", UriKind.Absolute);
        using var usageRequest = CreateClaudeWebRequest(usageUri, sessionKey);
        using var usageResponse = await httpClient.SendAsync(usageRequest, cancellationToken).ConfigureAwait(false);
        if (usageResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) {
            throw new InvalidOperationException("Claude rejected the web usage request. Refresh the session key and try again.");
        }
        if (!usageResponse.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                "Claude web usage request failed with HTTP "
                + ((int)usageResponse.StatusCode).ToString(CultureInfo.InvariantCulture)
                + ".");
        }

        using var usageStream = await usageResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var usageDocument = await JsonDocument.ParseAsync(usageStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var usageRoot = usageDocument.RootElement;

        var windows = new List<ProviderLimitWindow>();
        AddClaudeWindow(windows, "session", "Session", ReadClaudeUsageWindow(usageRoot, "five_hour"));
        AddClaudeWindow(windows, "weekly", "Weekly", ReadClaudeUsageWindow(usageRoot, "seven_day"));
        AddClaudeWindow(windows, "sonnet", "Sonnet weekly", ReadClaudeUsageWindow(usageRoot, "seven_day_sonnet"));
        AddClaudeWindow(windows, "opus", "Opus weekly", ReadClaudeUsageWindow(usageRoot, "seven_day_opus"));

        var summary = await TryFetchClaudeOverageSummaryAsync(httpClient, sessionKey, organization.Id, cancellationToken).ConfigureAwait(false);
        var account = await TryFetchClaudeAccountAsync(httpClient, sessionKey, organization.Id, cancellationToken).ConfigureAwait(false);

        return new ClaudeProviderSnapshot(
            account?.PlanLabel,
            account?.Email,
            windows,
            summary);
    }

    private static HttpClient CreateHttpClient() {
        var client = new HttpClient {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX", "0.1.0"));
        return client;
    }

    private static async Task<ClaudeOrganizationInfo?> FetchClaudeOrganizationAsync(
        HttpClient httpClient,
        string sessionKey,
        CancellationToken cancellationToken) {
        using var request = CreateClaudeWebRequest(ClaudeOrganizationsUri, sessionKey);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array) {
            foreach (var item in root.EnumerateArray()) {
                var id = NormalizeOptional(ReadString(item, "uuid"));
                if (id is not null) {
                    return new ClaudeOrganizationInfo(id);
                }
            }
        }

        if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "organizations", out var organizations) && organizations.ValueKind == JsonValueKind.Array) {
            foreach (var item in organizations.EnumerateArray()) {
                var id = NormalizeOptional(ReadString(item, "uuid"));
                if (id is not null) {
                    return new ClaudeOrganizationInfo(id);
                }
            }
        }

        return null;
    }

    private static HttpRequestMessage CreateClaudeWebRequest(Uri uri, string sessionKey) {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Cookie", "sessionKey=" + sessionKey);
        return request;
    }

    private static async Task<string?> TryFetchClaudeOverageSummaryAsync(
        HttpClient httpClient,
        string sessionKey,
        string organizationId,
        CancellationToken cancellationToken) {
        var uri = new Uri("https://claude.ai/api/organizations/" + Uri.EscapeDataString(organizationId) + "/overage_spend_limit", UriKind.Absolute);
        using var request = CreateClaudeWebRequest(uri, sessionKey);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var enabled = ReadBoolean(root, "is_enabled");
        if (enabled != true) {
            return null;
        }

        var limitCents = ReadDouble(root, "monthly_credit_limit");
        var usedCents = ReadDouble(root, "used_credits");
        var currency = NormalizeOptional(ReadString(root, "currency"));
        if (!limitCents.HasValue || !usedCents.HasValue || currency is null) {
            return null;
        }

        var used = usedCents.Value / 100d;
        var limit = limitCents.Value / 100d;
        return "Extra "
               + used.ToString("F2", CultureInfo.InvariantCulture)
               + " / "
               + limit.ToString("F2", CultureInfo.InvariantCulture)
               + " "
               + currency.ToUpperInvariant();
    }

    private static async Task<ClaudeAccountInfo?> TryFetchClaudeAccountAsync(
        HttpClient httpClient,
        string sessionKey,
        string organizationId,
        CancellationToken cancellationToken) {
        using var request = CreateClaudeWebRequest(ClaudeAccountUri, sessionKey);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var email = NormalizeOptional(ReadString(root, "email_address"));
        string? rateLimitTier = null;
        string? billingType = null;

        if (TryGetProperty(root, "memberships", out var memberships) && memberships.ValueKind == JsonValueKind.Array) {
            foreach (var membership in memberships.EnumerateArray()) {
                if (!TryGetProperty(membership, "organization", out var organization) || organization.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                var membershipOrgId = NormalizeOptional(ReadString(organization, "uuid"));
                if (membershipOrgId is null || !string.Equals(membershipOrgId, organizationId, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                rateLimitTier = NormalizeOptional(ReadString(organization, "rate_limit_tier"));
                billingType = NormalizeOptional(ReadString(organization, "billing_type"));
                break;
            }
        }

        return new ClaudeAccountInfo(email, InferClaudePlan(rateLimitTier, billingType));
    }

    private static void AddClaudeWindow(
        ICollection<ProviderLimitWindow> windows,
        string key,
        string label,
        ClaudeUsageWindow? window) {
        if (window is null) {
            return;
        }

        windows.Add(new ProviderLimitWindow(
            key,
            label,
            NormalizePercent(window.Utilization),
            window.ResetsAt));
    }

    private static ClaudeUsageWindow? ReadClaudeUsageWindow(JsonElement root, string propertyName) {
        if (!TryGetProperty(root, propertyName, out var window) || window.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var utilization = ReadDouble(window, "utilization");
        var resetsAt = ParseDateTimeOffset(ReadString(window, "resets_at"));
        if (!utilization.HasValue && !resetsAt.HasValue) {
            return null;
        }

        return new ClaudeUsageWindow(NormalizePercent(utilization), resetsAt);
    }

    private static string? FormatClaudeExtraUsage(JsonElement root) {
        if (!TryGetProperty(root, "extra_usage", out var extraUsage) || extraUsage.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var enabled = ReadBoolean(extraUsage, "is_enabled");
        if (enabled != true) {
            return null;
        }

        var usedCredits = ReadDouble(extraUsage, "used_credits");
        var monthlyLimit = ReadDouble(extraUsage, "monthly_limit");
        var currency = NormalizeOptional(ReadString(extraUsage, "currency"));
        if (!usedCredits.HasValue || !monthlyLimit.HasValue || currency is null) {
            return null;
        }

        return "Extra "
               + usedCredits.Value.ToString("F2", CultureInfo.InvariantCulture)
               + " / "
               + monthlyLimit.Value.ToString("F2", CultureInfo.InvariantCulture)
               + " "
               + currency.ToUpperInvariant();
    }

    private static string? ResolveClaudeSessionKeyFromEnvironment() {
        var candidates = new[] {
            Environment.GetEnvironmentVariable("INTELLIGENCEX_CLAUDE_SESSION_KEY"),
            Environment.GetEnvironmentVariable("CLAUDE_SESSION_KEY"),
            Environment.GetEnvironmentVariable("ANTHROPIC_SESSION_KEY"),
            Environment.GetEnvironmentVariable("INTELLIGENCEX_CLAUDE_COOKIE"),
            Environment.GetEnvironmentVariable("CLAUDE_COOKIE")
        };

        foreach (var candidate in candidates) {
            var parsed = ExtractSessionKey(candidate);
            if (!string.IsNullOrWhiteSpace(parsed)) {
                return parsed;
            }
        }

        return null;
    }

    private static string? ExtractSessionKey(string? candidate) {
        var normalized = NormalizeOptional(candidate);
        if (normalized is null) {
            return null;
        }

        if (normalized.StartsWith("sessionKey=", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeOptional(normalized.Substring("sessionKey=".Length));
        }

        foreach (var part in normalized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) {
            var trimmed = part.Trim();
            if (!trimmed.StartsWith("sessionKey=", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return NormalizeOptional(trimmed.Substring("sessionKey=".Length));
        }

        return normalized;
    }

    private static ClaudeCredentials? TryLoadClaudeCredentials() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            return null;
        }

        var path = Path.Combine(home, ".claude", ".credentials.json");
        if (!File.Exists(path)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!TryGetProperty(root, "claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object) {
                return null;
            }

            var scopes = Array.Empty<string>();
            if (TryGetProperty(oauth, "scopes", out var scopeElement) && scopeElement.ValueKind == JsonValueKind.Array) {
                scopes = scopeElement
                    .EnumerateArray()
                    .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToArray();
            }

            return new ClaudeCredentials(
                NormalizeOptional(ReadString(oauth, "accessToken")),
                scopes,
                NormalizeOptional(ReadString(oauth, "subscriptionType")),
                NormalizeOptional(ReadString(oauth, "rateLimitTier")));
        } catch {
            return null;
        }
    }

    private static string? InferClaudePlan(string? rateLimitTier, string? subscriptionType) {
        var tier = NormalizeOptional(rateLimitTier)?.ToLowerInvariant() ?? string.Empty;
        var subscription = NormalizeOptional(subscriptionType)?.ToLowerInvariant() ?? string.Empty;

        if (tier.Contains("max", StringComparison.Ordinal)) {
            return "Claude Max";
        }
        if (tier.Contains("pro", StringComparison.Ordinal)) {
            return "Claude Pro";
        }
        if (tier.Contains("team", StringComparison.Ordinal)) {
            return "Claude Team";
        }
        if (tier.Contains("enterprise", StringComparison.Ordinal)) {
            return "Claude Enterprise";
        }
        if (subscription.Contains("max", StringComparison.Ordinal)) {
            return "Claude Max";
        }
        if (subscription.Contains("pro", StringComparison.Ordinal)) {
            return "Claude Pro";
        }
        if (subscription.Contains("team", StringComparison.Ordinal)) {
            return "Claude Team";
        }
        if (subscription.Contains("enterprise", StringComparison.Ordinal)) {
            return "Claude Enterprise";
        }
        return null;
    }

    private sealed record ClaudeProviderSnapshot(
        string? PlanLabel,
        string? AccountLabel,
        IReadOnlyList<ProviderLimitWindow> Windows,
        string? Summary);

    private sealed record ClaudeUsageWindow(double? Utilization, DateTimeOffset? ResetsAt);

    private sealed record ClaudeOrganizationInfo(string Id);

    private sealed record ClaudeAccountInfo(string? Email, string? PlanLabel);

    private sealed record ClaudeCredentials(
        string? AccessToken,
        IReadOnlyList<string> Scopes,
        string? SubscriptionType,
        string? RateLimitTier);
}
