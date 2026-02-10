using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed partial class GitHubClient {
    private static bool LooksLikeGraphQlMutation(string queryText) {
        if (string.IsNullOrWhiteSpace(queryText)) {
            return false;
        }
        const string MutationKeyword = "mutation";
        var trimmed = TrimGraphQlLeadingTrivia(queryText);
        if (!trimmed.StartsWith(MutationKeyword, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (trimmed.Length == MutationKeyword.Length) {
            return true;
        }
        var next = trimmed[MutationKeyword.Length];
        return char.IsWhiteSpace(next) || next == '(';
    }

    private static string TrimGraphQlLeadingTrivia(string queryText) {
        if (string.IsNullOrEmpty(queryText)) {
            return string.Empty;
        }
        var i = 0;
        while (i < queryText.Length) {
            var ch = queryText[i];
            // BOM (can sneak into interpolated or generated strings).
            if (ch == '\uFEFF') {
                i++;
                continue;
            }
            if (char.IsWhiteSpace(ch)) {
                i++;
                continue;
            }
            // GraphQL comments start with '#'.
            if (ch == '#') {
                while (i < queryText.Length && queryText[i] != '\n') {
                    i++;
                }
                continue;
            }
            break;
        }
        return i == 0 ? queryText : queryText[i..];
    }

    private Task<JsonValue> PostGraphQlQueryAsync(JsonObject payload, CancellationToken cancellationToken, bool allowRetries) {
        var queryText = payload.GetString("query") ?? string.Empty;
        if (LooksLikeGraphQlMutation(queryText)) {
            throw new InvalidOperationException("GraphQL mutation detected in query helper. Use PostGraphQlMutationAsync instead.");
        }
        return PostGraphQlCoreAsync(payload, cancellationToken, allowRetries: allowRetries, throwOnErrors: false);
    }

    private Task<JsonValue> PostGraphQlMutationAsync(JsonObject payload, CancellationToken cancellationToken) {
        var queryText = payload.GetString("query") ?? string.Empty;
        if (!LooksLikeGraphQlMutation(queryText)) {
            throw new InvalidOperationException("GraphQL query detected in mutation helper. Use PostGraphQlQueryAsync instead.");
        }
        return PostGraphQlCoreAsync(payload, cancellationToken, allowRetries: false, throwOnErrors: true);
    }

    private async Task<JsonValue> PostGraphQlCoreAsync(JsonObject payload, CancellationToken cancellationToken, bool allowRetries, bool throwOnErrors) {
        return await WithGateAsync(async () => {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            const string url = "/graphql";
            var attempts = allowRetries ? DefaultRetryAttempts : 1;
            var retryBudgetStart = DateTimeOffset.UtcNow;
            for (var attempt = 1; attempt <= attempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false)) {
                        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (attempt < attempts && TryGetRetryDelay(response, responseText, attempt, isGraphQl: true, out var delay)) {
                            if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            // No retry budget left: surface the current response as an error.
                        }
                        if (!response.IsSuccessStatusCode) {
                            throw new InvalidOperationException(
                                FormatApiError("POST", url, response, responseText));
                        }
                        var parsed = JsonLite.Parse(responseText) ?? JsonValue.Null;
                        var errors = parsed.AsObject()?.GetArray("errors");
                        if (errors is not null && errors.Count > 0) {
                            if (throwOnErrors) {
                                throw new InvalidOperationException($"GitHub GraphQL request returned errors: {Truncate(responseText)}");
                            }
                            // Allow partial data for read-only queries, but fail fast when the response has no usable data.
                            var dataObj = parsed.AsObject()?.GetObject("data");
                            if (dataObj is null) {
                                throw new InvalidOperationException($"GitHub GraphQL request returned errors and no data: {Truncate(responseText)}");
                            }
                        }
                        return parsed;
                    }
                } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                } catch (HttpRequestException) when (attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException($"GitHub API request failed (POST {url}) after {attempts} attempts.");
        }, cancellationToken).ConfigureAwait(false);
    }
}

