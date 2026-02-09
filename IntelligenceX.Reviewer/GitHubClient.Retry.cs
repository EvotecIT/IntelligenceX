using System;
using System.Globalization;
using System.Net.Http;

namespace IntelligenceX.Reviewer;

internal sealed partial class GitHubClient {
    private const int MaxRetryDelaySeconds = 15;
    private static readonly TimeSpan RetryBudgetReserve = TimeSpan.FromMilliseconds(250);

    private static bool TryGetRetryDelay(HttpResponseMessage response, string responseText, int attempt, out TimeSpan delay) {
        return TryGetRetryDelay(response, responseText, attempt, isGraphQl: false, out delay);
    }

    private static bool TryGetRetryDelay(HttpResponseMessage response, string responseText, int attempt, bool isGraphQl, out TimeSpan delay) {
        delay = TimeSpan.Zero;
        var statusCode = (int)response.StatusCode;

        // Transient server errors.
        if (statusCode is 500 or 502 or 503 or 504) {
            delay = ComputeBackoff(attempt, maxSeconds: 8);
            return true;
        }

        // Rate limiting.
        if (statusCode == 429) {
            delay = ComputeRateLimitDelay(response, responseText, attempt);
            return true;
        }
        if (statusCode == 403 && LooksLikeRateLimitHeadersOnly(response)) {
            delay = ComputeRateLimitDelay(response, responseText, attempt);
            return true;
        }

        // GitHub GraphQL can return HTTP 200 with an `errors` payload for secondary rate limits / abuse detection.
        // Only treat this as retryable when we have high-confidence rate-limit indicators (headers or structured fields),
        // not by scanning arbitrary error text.
        if (isGraphQl && statusCode == 200 && LooksLikeGraphQlRateLimit(response, responseText)) {
            delay = ComputeRateLimitDelay(response, responseText, attempt);
            return true;
        }

        return false;
    }

    private static bool TryScheduleRetry(DateTimeOffset retryBudgetStart, ref TimeSpan delay) {
        var now = DateTimeOffset.UtcNow;
        var remaining = DefaultRetryBudgetWindow - (now - retryBudgetStart);
        if (remaining <= TimeSpan.Zero) {
            return false;
        }
        if (delay <= TimeSpan.Zero) {
            return false;
        }

        // Only schedule retries when we can wait the full delay and still keep a small reserve in the budget window.
        var remainingAfterDelay = remaining - delay;
        if (remainingAfterDelay <= RetryBudgetReserve) {
            return false;
        }
        return true;
    }

    private static bool LooksLikeRateLimit(HttpResponseMessage response, string responseText) {
        // Retry-After generally means "wait then retry" (abuse detection / secondary rate limits).
        if (TryParseRetryAfter(response, out _)) {
            return true;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)) {
            foreach (var item in values) {
                if (string.Equals(item?.Trim(), "0", StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return responseText.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
               responseText.Contains("secondary rate", StringComparison.OrdinalIgnoreCase) ||
               responseText.Contains("abuse detection", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRateLimitHeadersOnly(HttpResponseMessage response) {
        if (TryParseRetryAfter(response, out _)) {
            return true;
        }
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)) {
            foreach (var item in values) {
                if (string.Equals(item?.Trim(), "0", StringComparison.Ordinal)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool LooksLikeGraphQlRateLimit(HttpResponseMessage response, string responseText) {
        // Headers are high-confidence signals for retry (abuse detection / secondary rate limits).
        if (TryParseRetryAfter(response, out _)) {
            return true;
        }
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)) {
            foreach (var item in values) {
                if (string.Equals(item?.Trim(), "0", StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        // Fallback to structured error markers. Avoid scanning arbitrary text for "rate limit".
        try {
            var parsed = JsonLite.Parse(responseText);
            var errors = parsed?.AsObject()?.GetArray("errors");
            if (errors is null || errors.Count == 0) {
                return false;
            }
            foreach (var e in errors) {
                var obj = e.AsObject();
                if (obj is null) {
                    continue;
                }
                var type = obj.GetString("type") ?? string.Empty;
                if (string.Equals(type, "RATE_LIMITED", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
                var message = obj.GetString("message") ?? string.Empty;
                if (message.Contains("secondary rate", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("abuse detection", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("API rate limit exceeded", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("You have exceeded a secondary rate limit", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        } catch {
            // If the response isn't valid JSON, don't retry based on content heuristics.
        }
        return false;
    }

    private static TimeSpan ComputeRateLimitDelay(HttpResponseMessage response, string responseText, int attempt) {
        if (TryParseRetryAfter(response, out var retryAfter)) {
            // Keep a small floor so back-to-back retries don't hammer.
            return Clamp(retryAfter, minSeconds: 1, maxSeconds: MaxRetryDelaySeconds);
        }

        // If we have a reset time, wait until then (capped).
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)) {
            foreach (var item in values) {
                if (long.TryParse(item?.Trim(), out var seconds) && seconds > 0) {
                    var resetAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    var delta = resetAt - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero) {
                        return Clamp(delta, minSeconds: 1, maxSeconds: MaxRetryDelaySeconds);
                    }
                }
            }
        }

        // Fall back to exponential backoff.
        var backoff = ComputeBackoff(attempt, maxSeconds: 30);
        if (responseText.Contains("secondary rate", StringComparison.OrdinalIgnoreCase) ||
            responseText.Contains("abuse detection", StringComparison.OrdinalIgnoreCase)) {
            // Secondary rate limits tend to want slightly longer delays.
            backoff = TimeSpan.FromSeconds(Math.Min(MaxRetryDelaySeconds, Math.Max(5, backoff.TotalSeconds)));
        }
        return backoff;
    }

    private static bool TryParseRetryAfter(HttpResponseMessage response, out TimeSpan delay) {
        delay = TimeSpan.Zero;
        if (!response.Headers.TryGetValues("Retry-After", out var values)) {
            return false;
        }
        foreach (var item in values) {
            var raw = (item ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) {
                continue;
            }
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0) {
                delay = TimeSpan.FromSeconds(seconds);
                return true;
            }
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var date)) {
                var delta = date - DateTimeOffset.UtcNow;
                delay = delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
                return true;
            }
        }
        return false;
    }

    private static TimeSpan ComputeBackoff(int attempt, int maxSeconds) {
        // attempt is 1-based.
        var seconds = attempt switch {
            <= 1 => 1,
            2 => 2,
            3 => 4,
            _ => 8
        };
        return TimeSpan.FromSeconds(Math.Min(maxSeconds, seconds));
    }

    private static TimeSpan Clamp(TimeSpan value, int minSeconds, int maxSeconds) {
        var seconds = Math.Max(minSeconds, Math.Min(maxSeconds, value.TotalSeconds));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string FormatApiError(string method, string url, HttpResponseMessage response, string responseText) {
        var requestId = string.Empty;
        if (response.Headers.TryGetValues("x-github-request-id", out var values)) {
            foreach (var item in values) {
                if (!string.IsNullOrWhiteSpace(item)) {
                    requestId = item.Trim();
                    break;
                }
            }
        }
        var requestIdSuffix = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $" request_id={requestId}";
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? string.Empty : $" {response.ReasonPhrase}";
        return $"GitHub API request failed ({(int)response.StatusCode}{reason}) {method} {url}:{requestIdSuffix} {Truncate(responseText)}";
    }

    private static string Truncate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "<empty>";
        }
        const int Max = 2000;
        var trimmed = value.Trim();
        return trimmed.Length <= Max ? trimmed : (trimmed[..Max] + "...(truncated)");
    }
}
