using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Telemetry;

internal sealed class CopilotQuotaSnapshotClient : IDisposable {
    // These mirror the stable Copilot Chat request shape used by current GitHub Copilot clients.
    private const string EditorVersionHeaderValue = "vscode/1.96.2";
    private const string EditorPluginVersionHeaderValue = "copilot-chat/0.26.7";
    private const string UserAgentHeaderValue = "GitHubCopilotChat/0.26.7";
    private const string GitHubApiVersionHeaderValue = "2025-04-01";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _endpoint;

    public CopilotQuotaSnapshotClient(HttpClient? httpClient = null, string? apiBaseUrl = null) {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;

        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "https://api.github.com/"
            : apiBaseUrl!.Trim();
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal)) {
            baseUrl += "/";
        }

        _endpoint = new Uri(new Uri(baseUrl, UriKind.Absolute), "copilot_internal/user");
    }

    public async Task<CopilotQuotaSnapshot?> FetchAsync(string token, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(token)) {
            throw new InvalidOperationException("A GitHub token is required to fetch Copilot quota data.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Editor-Version", EditorVersionHeaderValue);
        request.Headers.TryAddWithoutValidation("Editor-Plugin-Version", EditorPluginVersionHeaderValue);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgentHeaderValue);
        request.Headers.TryAddWithoutValidation("X-Github-Api-Version", GitHubApiVersionHeaderValue);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) {
            throw new InvalidOperationException("GitHub rejected the Copilot quota request. Refresh the GitHub token and try again.");
        }

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                "GitHub Copilot quota request failed with HTTP "
                + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                + ".");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSnapshot(document.RootElement);
    }

    internal static CopilotQuotaSnapshot? ParseSnapshot(JsonElement root) {
        if (root.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var directSnapshots = TryGetProperty(root, "quota_snapshots", out var snapshotsElement) && snapshotsElement.ValueKind == JsonValueKind.Object
            ? ParseQuotaSnapshotContainer(snapshotsElement)
            : null;
        var monthlyQuotas = TryGetProperty(root, "monthly_quotas", out var monthlyElement) && monthlyElement.ValueKind == JsonValueKind.Object
            ? ParseQuotaCounts(monthlyElement)
            : null;
        var limitedUserQuotas = TryGetProperty(root, "limited_user_quotas", out var limitedElement) && limitedElement.ValueKind == JsonValueKind.Object
            ? ParseQuotaCounts(limitedElement)
            : null;
        var fallbackSnapshots = BuildFallbackQuotaSnapshots(monthlyQuotas, limitedUserQuotas);

        var premium = SelectPreferredQuota(directSnapshots?.PremiumInteractions, fallbackSnapshots?.PremiumInteractions);
        var chat = SelectPreferredQuota(directSnapshots?.Chat, fallbackSnapshots?.Chat);
        if (premium is null && chat is null && directSnapshots is not null) {
            premium = directSnapshots.PremiumInteractions;
            chat = directSnapshots.Chat;
        }

        var plan = ReadString(root, "copilot_plan") ?? "unknown";
        var assignedDateRaw = ReadString(root, "assigned_date");
        var quotaResetDateRaw = ReadString(root, "quota_reset_date");

        return new CopilotQuotaSnapshot(
            plan,
            assignedDateRaw,
            ParseDateTimeOffset(assignedDateRaw),
            quotaResetDateRaw,
            ParseDateTimeOffset(quotaResetDateRaw),
            premium,
            chat);
    }

    private static CopilotQuotaSnapshotContainer? ParseQuotaSnapshotContainer(JsonElement container) {
        CopilotQuotaWindow? premium = null;
        CopilotQuotaWindow? chat = null;
        CopilotQuotaWindow? firstUsable = null;

        foreach (var property in container.EnumerateObject()) {
            var parsed = ParseQuotaWindow(property.Value, property.Name);
            if (parsed is null) {
                continue;
            }

            firstUsable ??= parsed;
            var name = property.Name;
            if (chat is null && name.Contains("chat", StringComparison.OrdinalIgnoreCase)) {
                chat = parsed;
                continue;
            }

            if (premium is null &&
                (name.Contains("premium", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("completion", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("code", StringComparison.OrdinalIgnoreCase))) {
                premium = parsed;
            }
        }

        if (premium is null && chat is null) {
            chat = firstUsable;
        }

        return premium is null && chat is null
            ? null
            : new CopilotQuotaSnapshotContainer(premium, chat);
    }

    private static CopilotQuotaCounts? ParseQuotaCounts(JsonElement container) {
        if (container.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return new CopilotQuotaCounts(
            ReadDouble(container, "chat"),
            ReadDouble(container, "completions"));
    }

    private static CopilotQuotaSnapshotContainer? BuildFallbackQuotaSnapshots(
        CopilotQuotaCounts? monthlyQuotas,
        CopilotQuotaCounts? limitedUserQuotas) {
        var premium = BuildQuotaWindow(
            monthlyQuotas?.Completions,
            limitedUserQuotas?.Completions,
            "completions");
        var chat = BuildQuotaWindow(
            monthlyQuotas?.Chat,
            limitedUserQuotas?.Chat,
            "chat");

        return premium is null && chat is null
            ? null
            : new CopilotQuotaSnapshotContainer(premium, chat);
    }

    private static CopilotQuotaWindow? BuildQuotaWindow(double? entitlement, double? remaining, string quotaId) {
        if (!entitlement.HasValue || !remaining.HasValue) {
            return null;
        }

        var total = Math.Max(0d, entitlement.Value);
        if (total <= 0d) {
            return null;
        }

        var left = Math.Max(0d, remaining.Value);
        var percentRemaining = Math.Clamp(left / total * 100d, 0d, 100d);
        return new CopilotQuotaWindow(quotaId, total, left, percentRemaining, true);
    }

    private static CopilotQuotaWindow? ParseQuotaWindow(JsonElement element, string fallbackQuotaId) {
        if (element.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var entitlement = ReadDouble(element, "entitlement") ?? 0d;
        var remaining = ReadDouble(element, "remaining") ?? 0d;
        var percentRemaining = ReadDouble(element, "percent_remaining");
        var quotaId = ReadString(element, "quota_id") ?? fallbackQuotaId;

        var hasPercentRemaining = false;
        double normalizedPercent;
        if (percentRemaining.HasValue) {
            normalizedPercent = Math.Clamp(percentRemaining.Value, 0d, 100d);
            hasPercentRemaining = true;
        } else if (entitlement > 0d) {
            normalizedPercent = Math.Clamp(remaining / entitlement * 100d, 0d, 100d);
            hasPercentRemaining = true;
        } else {
            normalizedPercent = 0d;
        }

        var isPlaceholder = entitlement <= 0d
                            && remaining <= 0d
                            && normalizedPercent <= 0d
                            && string.IsNullOrWhiteSpace(quotaId);
        if (isPlaceholder) {
            return null;
        }

        return new CopilotQuotaWindow(quotaId, entitlement, remaining, normalizedPercent, hasPercentRemaining);
    }

    private static CopilotQuotaWindow? SelectPreferredQuota(CopilotQuotaWindow? direct, CopilotQuotaWindow? fallback) {
        if (direct is not null && direct.HasPercentRemaining) {
            return direct;
        }
        if (fallback is not null && fallback.HasPercentRemaining) {
            return fallback;
        }
        return direct ?? fallback;
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

    private static double? ReadDouble(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var property)) {
            return null;
        }

        return property.ValueKind switch {
            JsonValueKind.Number when property.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property) {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property)) {
            return true;
        }

        property = default;
        return false;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            return parsed;
        }

        return null;
    }

    public void Dispose() {
        if (_ownsHttpClient) {
            _httpClient.Dispose();
        }
    }
}

internal sealed record CopilotQuotaSnapshot(
    string Plan,
    string? AssignedDateRaw,
    DateTimeOffset? AssignedDate,
    string? QuotaResetDateRaw,
    DateTimeOffset? QuotaResetDate,
    CopilotQuotaWindow? PremiumInteractions,
    CopilotQuotaWindow? Chat);

internal sealed record CopilotQuotaWindow(
    string QuotaId,
    double Entitlement,
    double Remaining,
    double PercentRemaining,
    bool HasPercentRemaining) {
    public double UsedPercent => Math.Clamp(100d - PercentRemaining, 0d, 100d);
}

internal sealed record CopilotQuotaCounts(double? Chat, double? Completions);

internal sealed record CopilotQuotaSnapshotContainer(
    CopilotQuotaWindow? PremiumInteractions,
    CopilotQuotaWindow? Chat);
