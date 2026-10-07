using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

internal sealed partial class ChatGptUsageClient {
    internal async Task<ChatGptAccountAnalytics> GetAccountAnalyticsAsync(string baseUrl, string accessToken, string? accountId,
        string? userAgent, CancellationToken cancellationToken) {
        var normalized = NormalizeBaseUrl(baseUrl, out var style);
        var root = normalized + (style == ChatGptUsagePathStyle.ChatGptApi ? "/wham" : "/api/codex");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var results = await Task.WhenAll(
            ReadAnalyticsEndpointAsync("profile", root + "/profiles/me", "stats"),
            ReadAnalyticsEndpointAsync("daily_usage", root + "/usage/daily-token-usage-breakdown", "data"),
            ReadAnalyticsEndpointAsync("plan_history", root + "/usage/plan_limit_history?days=30", "periods")).ConfigureAwait(false);
        var obj = new JsonObject().Add("retrieved_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var errors = new JsonArray();
        foreach (var result in results) {
            if (result.Data is not null) obj.Add(result.Name, result.Data);
            if (result.Error is not null) errors.Add(JsonValue.From(result.Error));
        }
        obj.Add("errors", errors);
        return ChatGptAccountAnalytics.FromJson(obj);

        async Task<(string Name, JsonObject? Data, JsonObject? Error)> ReadAnalyticsEndpointAsync(string name, string url, string requiredField) {
            long? statusCode = null;
            try {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyHeaders(request, accessToken, accountId, userAgent);
                using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                statusCode = (int)response.StatusCode;
                if (response.IsSuccessStatusCode) {
                    var payload = await ReadAsStringAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    var data = JsonLite.Parse(payload)?.AsObject();
                    var recognized = requiredField == "stats" ? data?.GetObject(requiredField) is not null : data?.GetArray(requiredField) is not null;
                    if (recognized) return (name, data, null);
                }
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                statusCode = null;
            } catch (Exception ex) when (ex is HttpRequestException or FormatException or InvalidOperationException) {
                statusCode = null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return (name, null, new JsonObject().Add("endpoint", name).Add("status_code", statusCode.HasValue ? JsonValue.From(statusCode.Value) : JsonValue.Null)
                .Add("message", "Provider " + name + " data is unavailable for this account."));
        }
    }

    internal async Task<ChatGptThreadUsage> QueryThreadUsageAsync(string baseUrl, string accessToken, string? accountId,
        string? userAgent, JsonObject query, CancellationToken cancellationToken) {
        var normalized = NormalizeBaseUrl(baseUrl, out var style);
        var root = normalized + (style == ChatGptUsagePathStyle.ChatGptApi ? "/wham" : "/api/codex");
        using var request = new HttpRequestMessage(HttpMethod.Post, root + "/usage/thread_usage/query_v2") {
            Content = new StringContent(JsonLite.Serialize(JsonValue.From(query)), Encoding.UTF8, "application/json")
        };
        ApplyHeaders(request, accessToken, accountId, userAgent);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Provider thread usage is unavailable (HTTP " + (int)response.StatusCode + ").");
        var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        var obj = JsonLite.Parse(payload)?.AsObject();
        if (obj?.GetArray("threads") is null) throw new InvalidOperationException("Invalid provider thread usage response.");
        return ChatGptThreadUsage.FromJson(obj);
    }
}
