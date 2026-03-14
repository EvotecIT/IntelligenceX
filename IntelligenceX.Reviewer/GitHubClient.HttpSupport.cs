using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed partial class GitHubClient {
    private static PullRequestReviewThreadComment ParseReviewThreadComment(JsonObject commentObj) {
        var body = commentObj.GetString("body") ?? string.Empty;
        var author = commentObj.GetObject("author")?.GetString("login");
        var path = commentObj.GetString("path");
        var line = commentObj.GetInt64("line");
        var databaseId = commentObj.GetInt64("databaseId");
        var createdAtRaw = commentObj.GetString("createdAt");
        DateTimeOffset? createdAt = null;
        if (!string.IsNullOrWhiteSpace(createdAtRaw) &&
            DateTimeOffset.TryParse(
                createdAtRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)) {
            createdAt = parsed;
        }

        return new PullRequestReviewThreadComment(
            databaseId,
            createdAt,
            body,
            author,
            path,
            line.HasValue ? (int?)line.Value : null);
    }

    private async Task<JsonValue> GetJsonAsync(string url, CancellationToken cancellationToken) {
        return await WithGateAsync(async () => {
            var retryBudgetStart = DateTimeOffset.UtcNow;
            Exception? lastError = null;
            for (var attempt = 1; attempt <= DefaultRetryAttempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (attempt < DefaultRetryAttempts && TryGetRetryDelay(response, content, attempt, out var delay)) {
                        if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                            lastError = new InvalidOperationException(FormatApiError("GET", url, response, content));
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (!response.IsSuccessStatusCode) {
                        throw new InvalidOperationException(FormatApiError("GET", url, response, content));
                    }

                    return JsonLite.Parse(content) ?? JsonValue.Null;
                } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < DefaultRetryAttempts) {
                    lastError = ex;
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                } catch (HttpRequestException ex) when (attempt < DefaultRetryAttempts) {
                    lastError = ex;
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }

            if (lastError is not null) {
                throw new InvalidOperationException(
                    $"GitHub API request failed (GET {url}) after {DefaultRetryAttempts} attempts.",
                    lastError);
            }

            throw new InvalidOperationException($"GitHub API request failed (GET {url}) after {DefaultRetryAttempts} attempts.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        return PostJsonAsync(url, payload, cancellationToken, allowRetries: false);
    }

    private async Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken, bool allowRetries) {
        return await WithGateAsync(async () => {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            var attempts = allowRetries ? DefaultRetryAttempts : 1;
            var retryBudgetStart = DateTimeOffset.UtcNow;
            Exception? lastError = null;
            for (var attempt = 1; attempt <= attempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                    var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (attempt < attempts && TryGetRetryDelay(response, responseText, attempt, out var delay)) {
                        if (!response.IsSuccessStatusCode) {
                            lastError = new InvalidOperationException(FormatApiError("POST", url, response, responseText));
                        }

                        if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (!response.IsSuccessStatusCode) {
                        throw new InvalidOperationException(FormatApiError("POST", url, response, responseText));
                    }

                    return JsonLite.Parse(responseText) ?? JsonValue.Null;
                } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < attempts) {
                    lastError = ex;
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                } catch (HttpRequestException ex) when (attempt < attempts) {
                    lastError = ex;
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }

            if (lastError is not null) {
                throw new InvalidOperationException($"GitHub API request failed (POST {url}) after {attempts} attempts.", lastError);
            }

            throw new InvalidOperationException($"GitHub API request failed (POST {url}) after {attempts} attempts.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task PatchJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        await WithGateAsync(async () => {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            cancellationToken.ThrowIfCancellationRequested();
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException(FormatApiError("PATCH", url, response, responseText));
            }

            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }
}
