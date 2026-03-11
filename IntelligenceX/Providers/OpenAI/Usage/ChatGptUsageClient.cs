using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

internal enum ChatGptUsagePathStyle {
    CodexApi,
    ChatGptApi
}

internal sealed class ChatGptUsageClient : IDisposable {
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _httpClient = SharedClient;

    public async Task<ChatGptUsageSnapshot> GetUsageAsync(string baseUrl, string accessToken, string? accountId, string? userAgent,
        CancellationToken cancellationToken) {
        var normalized = NormalizeBaseUrl(baseUrl, out var style);
        var url = BuildUsageUrl(normalized, style);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, accessToken, accountId, userAgent);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"ChatGPT usage request failed ({(int)response.StatusCode}): {payload}");
        }
        var obj = JsonLite.Parse(payload)?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid ChatGPT usage response.");
        }
        return ChatGptUsageSnapshot.FromJson(obj);
    }

    public async Task<IReadOnlyList<ChatGptCreditUsageEvent>> GetCreditUsageEventsAsync(string baseUrl, string accessToken, string? accountId,
        string? userAgent, CancellationToken cancellationToken) {
        var normalized = NormalizeBaseUrl(baseUrl, out var style);
        var url = BuildCreditUsageUrl(normalized, style);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, accessToken, accountId, userAgent);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"ChatGPT credit usage request failed ({(int)response.StatusCode}): {payload}");
        }
        var obj = JsonLite.Parse(payload)?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid ChatGPT credit usage response.");
        }
        var data = obj.GetArray("data");
        if (data is null) {
            return Array.Empty<ChatGptCreditUsageEvent>();
        }
        var result = new List<ChatGptCreditUsageEvent>();
        foreach (var entry in data.Select(item => item.AsObject()).Where(obj => obj is not null)) {
            result.Add(ChatGptCreditUsageEvent.FromJson(entry!));
        }
        return result;
    }

    public async Task<ChatGptDailyTokenUsageBreakdown> GetDailyTokenUsageBreakdownAsync(string baseUrl, string accessToken, string? accountId,
        string? userAgent, CancellationToken cancellationToken) {
        var normalized = NormalizeBaseUrl(baseUrl, out var style);
        var url = BuildDailyTokenUsageBreakdownUrl(normalized, style);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, accessToken, accountId, userAgent);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"ChatGPT daily token usage request failed ({(int)response.StatusCode}): {payload}");
        }
        var obj = JsonLite.Parse(payload)?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid ChatGPT daily token usage response.");
        }
        return ChatGptDailyTokenUsageBreakdown.FromJson(obj);
    }

    private static string NormalizeBaseUrl(string baseUrl, out ChatGptUsagePathStyle style) {
        var normalized = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim();
        while (normalized.EndsWith("/", StringComparison.Ordinal)) {
            normalized = normalized.Substring(0, normalized.Length - 1);
        }
        if ((normalized.StartsWith("https://chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
             normalized.StartsWith("https://chat.openai.com", StringComparison.OrdinalIgnoreCase)) &&
            normalized.IndexOf("/backend-api", StringComparison.OrdinalIgnoreCase) < 0) {
            normalized = normalized + "/backend-api";
        }
        style = normalized.IndexOf("/backend-api", StringComparison.OrdinalIgnoreCase) >= 0
            ? ChatGptUsagePathStyle.ChatGptApi
            : ChatGptUsagePathStyle.CodexApi;
        return normalized;
    }

    private static string BuildUsageUrl(string baseUrl, ChatGptUsagePathStyle style) {
        return style == ChatGptUsagePathStyle.ChatGptApi
            ? $"{baseUrl}/wham/usage"
            : $"{baseUrl}/api/codex/usage";
    }

    private static string BuildCreditUsageUrl(string baseUrl, ChatGptUsagePathStyle style) {
        return style == ChatGptUsagePathStyle.ChatGptApi
            ? $"{baseUrl}/wham/usage/credit-usage-events"
            : $"{baseUrl}/api/codex/usage/credit-usage-events";
    }

    private static string BuildDailyTokenUsageBreakdownUrl(string baseUrl, ChatGptUsagePathStyle style) {
        return style == ChatGptUsagePathStyle.ChatGptApi
            ? $"{baseUrl}/wham/usage/daily-token-usage-breakdown"
            : $"{baseUrl}/api/codex/usage/daily-token-usage-breakdown";
    }

    private static void ApplyHeaders(HttpRequestMessage request, string accessToken, string? accountId, string? userAgent) {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        if (!string.IsNullOrWhiteSpace(accountId)) {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        }
        var agent = string.IsNullOrWhiteSpace(userAgent) ? BuildDefaultUserAgent() : userAgent!;
        request.Headers.TryAddWithoutValidation("User-Agent", agent);
        request.Headers.TryAddWithoutValidation("accept", "application/json");
    }

    private static string BuildDefaultUserAgent() {
        try {
            return $"intelligencex ({Environment.OSVersion.VersionString})";
        } catch {
            return "intelligencex";
        }
    }

    private static HttpClient CreateClient() {
        return new HttpClient {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static Task<string> ReadAsStringAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
#else
        return content.ReadAsStringAsync(cancellationToken);
#endif
    }

    public void Dispose() {
        // Shared client; do not dispose.
    }
}
