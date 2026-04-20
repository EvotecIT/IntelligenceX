using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.Telemetry;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewDiagnosticsSnapshot {
    public ReviewDiagnosticsSnapshot(IReadOnlyList<string> standardError, Exception? lastRpcError, string? lastRpcMethod,
        TimeSpan? lastRpcDuration, long? lastRpcRequestId) {
        StandardError = standardError;
        LastRpcError = lastRpcError;
        LastRpcMethod = lastRpcMethod;
        LastRpcDuration = lastRpcDuration;
        LastRpcRequestId = lastRpcRequestId;
    }

    public IReadOnlyList<string> StandardError { get; }
    public Exception? LastRpcError { get; }
    public string? LastRpcMethod { get; }
    public TimeSpan? LastRpcDuration { get; }
    public long? LastRpcRequestId { get; }
}

internal sealed class ReviewDiagnosticsSession : IDisposable {
    private const int MaxLines = 8;
    private readonly ReviewSettings _settings;
    private readonly IntelligenceXClient _client;
    private readonly Queue<string> _stderr = new();
    private readonly object _lock = new();
    private Exception? _lastRpcError;
    private string? _lastRpcMethod;
    private TimeSpan? _lastRpcDuration;
    private long? _lastRpcRequestId;
    private bool _disposed;

    private ReviewDiagnosticsSession(ReviewSettings settings, IntelligenceXClient client) {
        _settings = settings;
        _client = client;
        _client.StandardErrorReceived += OnStandardErrorReceived;
        _client.RpcCallCompleted += OnRpcCallCompleted;
    }

    public static ReviewDiagnosticsSession? TryStart(ReviewSettings settings, IntelligenceXClient client) {
        return settings.Diagnostics ? new ReviewDiagnosticsSession(settings, client) : null;
    }

    private void OnStandardErrorReceived(object? sender, string line) {
        AddLine(_stderr, line);
    }

    private void OnRpcCallCompleted(object? sender, RpcCallCompletedEventArgs args) {
        if (args.Success) {
            return;
        }
        lock (_lock) {
            _lastRpcError = args.Error;
            _lastRpcMethod = args.Method;
            _lastRpcDuration = args.Duration;
            _lastRpcRequestId = args.RequestId;
        }
    }

    private void AddLine(Queue<string> queue, string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return;
        }
        var trimmed = line.Trim();
        if (trimmed.Length == 0) {
            return;
        }
        lock (_lock) {
            queue.Enqueue(trimmed);
            while (queue.Count > MaxLines) {
                queue.Dequeue();
            }
        }
    }

    public ReviewDiagnosticsSnapshot Snapshot() {
        lock (_lock) {
            return new ReviewDiagnosticsSnapshot(new List<string>(_stderr), _lastRpcError, _lastRpcMethod, _lastRpcDuration, _lastRpcRequestId);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _client.StandardErrorReceived -= OnStandardErrorReceived;
        _client.RpcCallCompleted -= OnRpcCallCompleted;
    }
}

internal static class ReviewDiagnostics {
    public const string FailureMarker = "<!-- intelligencex:failure -->";
    internal const string WorkflowSummaryMarker = "<!-- intelligencex:summary -->";
    private const string InvalidConfigurationSummary = "Invalid configuration";
    private const string AuthBundleSummary = "Auth bundle missing or invalid";
    private const string AuthRefreshFailedSummary = "OpenAI auth refresh failed; sign in again";
    private const string AuthRefreshTokenReusedSummary = "OpenAI auth refresh token was already used; sign in again";
    private const string UsageBudgetGuardPrefix = "Usage budget guard blocked review run:";

    internal readonly record struct WorkflowFailureInfo(string Kind, string Label, string Detail, bool RequiresAuthRemediation);

    /// <summary>
    /// Categorizes reviewer failures for reporting and retry logic.
    /// </summary>
    public enum ReviewErrorCategory {
        /// <summary>Review cancelled by user or host.</summary>
        Cancelled,
        /// <summary>Operation timed out.</summary>
        Timeout,
        /// <summary>Provider rate limit hit.</summary>
        RateLimit,
        /// <summary>Provider service unavailable.</summary>
        ServiceUnavailable,
        /// <summary>Authentication failure.</summary>
        Auth,
        /// <summary>Configuration or request validation failure.</summary>
        Config,
        /// <summary>Network or transport failure.</summary>
        Network,
        /// <summary>Response ended unexpectedly (connection reset).</summary>
        ResponseEnded,
        /// <summary>Provider-specific failure not captured above.</summary>
        Provider,
        /// <summary>Unknown failure.</summary>
        Unknown
    }

    /// <summary>
    /// Structured summary of a classified review failure.
    /// </summary>
    public readonly record struct ReviewErrorInfo(ReviewErrorCategory Category, bool IsTransient, string Summary);

    public static bool IsFailureBody(string? body) {
        return !string.IsNullOrWhiteSpace(body) &&
               body.Contains(FailureMarker, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsResponseEnded(Exception ex) {
        if (ex.Message.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return ex.InnerException is not null && IsResponseEnded(ex.InnerException);
    }

    public static ReviewErrorInfo Classify(Exception ex) {
        var root = Unwrap(ex);
        if (root is OperationCanceledException) {
            return new ReviewErrorInfo(ReviewErrorCategory.Cancelled, false, "Cancelled");
        }
        if (root is TimeoutException) {
            return new ReviewErrorInfo(ReviewErrorCategory.Timeout, true, "Timeout");
        }
        if (IsResponseEnded(root)) {
            return new ReviewErrorInfo(ReviewErrorCategory.ResponseEnded, true, "Response ended prematurely");
        }
        var message = root.Message ?? string.Empty;
        if (root is UnauthorizedAccessException) {
            if (IsCopilotAuthMessage(message)) {
                return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, "Copilot authentication failed");
            }
            return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, "Unauthorized");
        }
        if (root is HttpRequestException httpRequest) {
            if (httpRequest.StatusCode.HasValue) {
                var code = (int)httpRequest.StatusCode.Value;
                return ClassifyStatusCode(code);
            }
            return new ReviewErrorInfo(ReviewErrorCategory.Network, true, "Network error");
        }
        if (root is IOException) {
            return new ReviewErrorInfo(ReviewErrorCategory.Network, true, "I/O error");
        }
        if (message.Contains(UsageBudgetGuardPrefix, StringComparison.OrdinalIgnoreCase)) {
            return new ReviewErrorInfo(ReviewErrorCategory.Config, false, ExtractUsageBudgetGuardDetail(message));
        }
        if (message.Contains("auth bundle", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("INTELLIGENCEX_AUTH", StringComparison.OrdinalIgnoreCase)) {
            return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, AuthBundleSummary);
        }
        if (message.Contains("refresh_token_reused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("refresh token has already been used", StringComparison.OrdinalIgnoreCase)) {
            return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, AuthRefreshTokenReusedSummary);
        }
        if ((message.Contains("OAuth token request failed", StringComparison.OrdinalIgnoreCase) &&
             message.Contains("signing in again", StringComparison.OrdinalIgnoreCase)) ||
            message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("token refresh", StringComparison.OrdinalIgnoreCase)) {
            return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, AuthRefreshFailedSummary);
        }
        if (IsCopilotAuthMessage(message)) {
            return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, "Copilot authentication failed");
        }
        if (message.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid", StringComparison.OrdinalIgnoreCase)) {
            return new ReviewErrorInfo(ReviewErrorCategory.Config, false, InvalidConfigurationSummary);
        }
        return new ReviewErrorInfo(ReviewErrorCategory.Unknown, false, root.GetType().Name);
    }

    public static string FormatExceptionSummary(Exception ex, bool includeInner) {
        var sb = new StringBuilder();
        AppendException(sb, ex, includeInner, 0);
        return sb.ToString();
    }

    public static string BuildFailureBody(Exception ex, ReviewSettings settings, ReviewDiagnosticsSnapshot? snapshot,
        ReviewRetryState? retryState, string? remediationRepo = null) {
        var classification = Classify(ex);
        var sb = new StringBuilder();
        sb.AppendLine(FailureMarker);
        sb.AppendLine("WARNING: Review failed to complete due to a provider request error.");
        sb.AppendLine();
        sb.AppendLine($"- Provider: {DescribeProvider(settings)}");
        sb.AppendLine($"- Transport: {DescribeTransport(settings)}");
        sb.AppendLine($"- Model: {DescribeModel(settings)}");
        sb.AppendLine($"- Category: {classification.Category} ({(classification.IsTransient ? "transient" : "non-transient")})");
        if (!string.IsNullOrWhiteSpace(classification.Summary)) {
            sb.AppendLine($"- Detail: {classification.Summary}");
        }
        if (retryState is not null && retryState.LastAttempt > 0) {
            sb.AppendLine($"- Retry: {retryState.LastAttempt}/{retryState.MaxAttempts}");
        }
        if (settings.Diagnostics) {
            sb.AppendLine("- Error details were written to the workflow logs.");
        }
        if (settings.Diagnostics && snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.LastRpcMethod)) {
            var lastRpc = snapshot.LastRpcMethod;
            if (snapshot.LastRpcRequestId.HasValue) {
                lastRpc = $"{lastRpc} (id: {snapshot.LastRpcRequestId.Value})";
            }
            sb.AppendLine($"- Last RPC: {lastRpc}");
        }
        var provider = ReviewProviderContracts.Get(settings.Provider);
        if (classification.Category == ReviewErrorCategory.Auth &&
            provider.RequiresOpenAiAuthStore) {
            var remediationCommand = BuildAuthRemediationCommand(remediationRepo);
            var authLabel = settings.OpenAITransport == OpenAITransportKind.Native
                ? "OpenAI native auth"
                : "OpenAI auth";
            sb.AppendLine();
            sb.AppendLine($"> {authLabel} is missing, expired, or stale for this reviewer run.");
            sb.AppendLine("> Reauthenticate locally and refresh `INTELLIGENCEX_AUTH_B64` with:");
            sb.AppendLine($"> `{remediationCommand}`");
        } else if (classification.Category == ReviewErrorCategory.Auth &&
                   settings.Provider == ReviewProvider.Copilot) {
            sb.AppendLine();
            sb.AppendLine("> Copilot CLI authentication is missing or not usable in this non-interactive runner.");
            sb.AppendLine("> Sign in on the runner, use a self-hosted runner with a persisted Copilot CLI session, or configure Copilot direct transport.");
        }
        sb.AppendLine();
        sb.AppendLine("_Re-run the workflow once connectivity is restored. Set `REVIEW_FAIL_OPEN=false` to keep failures blocking._");
        return sb.ToString().TrimEnd();
    }

    public static void LogFailure(Exception ex, ReviewSettings settings, ReviewDiagnosticsSnapshot? snapshot,
        ReviewRetryState? retryState) {
        var classification = Classify(ex);
        Console.Error.WriteLine("Provider request failed.");
        Console.Error.WriteLine($"Provider: {DescribeProvider(settings)} | Transport: {DescribeTransport(settings)} | Model: {DescribeModel(settings)}");
        Console.Error.WriteLine($"Category: {classification.Category} ({(classification.IsTransient ? "transient" : "non-transient")})");
        if (retryState is not null && retryState.LastAttempt > 0) {
            Console.Error.WriteLine($"Retry: {retryState.LastAttempt}/{retryState.MaxAttempts}");
        }
        var summary = FormatExceptionSummary(ex, settings.Diagnostics);
        if (!string.IsNullOrWhiteSpace(summary)) {
            Console.Error.WriteLine($"Cause: {summary}");
        }
        if (IsResponseEnded(ex)) {
            Console.Error.WriteLine("Hint: Response ended prematurely; network/proxy instability or HTTP/2 resets can cause this.");
        }

        if (settings.OpenAITransport == OpenAITransportKind.AppServer) {
            var path = settings.CodexPath ?? Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH");
            var args = settings.CodexArgs ?? Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS");
            if (string.IsNullOrWhiteSpace(path)) {
                Console.Error.WriteLine("Hint: set CODEX_APP_SERVER_PATH to the Codex app-server executable.");
            } else if (!File.Exists(path)) {
                Console.Error.WriteLine($"Hint: CODEX_APP_SERVER_PATH points to a missing file: {path}");
            }
            if (string.IsNullOrWhiteSpace(args)) {
                Console.Error.WriteLine("Hint: set CODEX_APP_SERVER_ARGS to the app-server arguments.");
            }
        } else {
            var authPath = AuthPaths.ResolveAuthPath();
            var authExists = File.Exists(authPath);
            Console.Error.WriteLine($"Auth bundle: {authPath} ({(authExists ? "found" : "missing")}).");
            if (!authExists) {
                Console.Error.WriteLine("Hint: run `intelligencex auth login` or set INTELLIGENCEX_AUTH_JSON/INTELLIGENCEX_AUTH_B64.");
            } else if (classification.Category == ReviewErrorCategory.Auth) {
                Console.Error.WriteLine($"Hint: refresh reviewer auth with `{BuildAuthRemediationCommand()}`.");
            }
        }

        if (!settings.Diagnostics || snapshot is null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastRpcMethod)) {
            var rpcSummary = snapshot.LastRpcDuration.HasValue
                ? $"{snapshot.LastRpcMethod} ({snapshot.LastRpcDuration.Value.TotalMilliseconds:0} ms)"
                : snapshot.LastRpcMethod;
            if (snapshot.LastRpcRequestId.HasValue) {
                rpcSummary += $" id={snapshot.LastRpcRequestId.Value}";
            }
            Console.Error.WriteLine($"Last RPC: {rpcSummary}");
        }
        if (snapshot.LastRpcError is not null) {
            Console.Error.WriteLine($"RPC error: {FormatExceptionSummary(snapshot.LastRpcError, true)}");
        }
        if (snapshot.StandardError.Count > 0) {
            Console.Error.WriteLine("Provider stderr (most recent first):");
            for (var i = snapshot.StandardError.Count - 1; i >= 0; i--) {
                Console.Error.WriteLine($"  {snapshot.StandardError[i]}");
            }
        }
    }

    internal static string DescribeProvider(ReviewSettings settings) {
        return ReviewProviderContracts.Get(settings.Provider).Id;
    }

    internal static string DescribeTransport(ReviewSettings settings) {
        return settings.Provider switch {
            ReviewProvider.Copilot => settings.CopilotTransport switch {
                CopilotTransportKind.Direct => "direct",
                _ => "cli"
            },
            ReviewProvider.OpenAI => settings.OpenAITransport switch {
                OpenAITransportKind.Native => "native",
                _ => "appserver"
            },
            ReviewProvider.OpenAICompatible => "http",
            ReviewProvider.Claude => "messages-api",
            _ => string.Empty
        };
    }

    internal static string DescribeModel(ReviewSettings settings) {
        if (settings.Provider == ReviewProvider.Copilot) {
            var model = ReviewRunner.ResolveCopilotModel(settings);
            if (!string.IsNullOrWhiteSpace(model)) {
                return model!;
            }
            return settings.CopilotTransport == CopilotTransportKind.Direct
                ? "Copilot direct model required"
                : "Copilot CLI default";
        }
        return settings.Model;
    }

    private static bool IsCopilotAuthMessage(string message) {
        return message.Contains("Copilot", StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("sign in", StringComparison.OrdinalIgnoreCase));
    }

    internal static WorkflowFailureInfo ClassifyWorkflowFailureLog(string? logText) {
        var text = logText ?? string.Empty;
        if (text.IndexOf(UsageBudgetGuardPrefix, StringComparison.OrdinalIgnoreCase) >= 0) {
            return new WorkflowFailureInfo(
                "usage-budget-guard",
                "Usage budget guard blocked the review",
                ExtractUsageBudgetGuardDetail(text),
                false);
        }

        if (text.IndexOf("refresh_token_reused", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("refresh token has already been used", StringComparison.OrdinalIgnoreCase) >= 0) {
            return new WorkflowFailureInfo(
                "openai-auth-refresh-reused",
                "OpenAI auth refresh token was already used",
                "OpenAI auth refresh token was already used; sign in again.",
                true);
        }

        if (text.IndexOf("OAuth token request failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("auth bundle", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("INTELLIGENCEX_AUTH_B64", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("signing in again", StringComparison.OrdinalIgnoreCase) >= 0) {
            return new WorkflowFailureInfo(
                "openai-auth",
                "OpenAI auth bundle is missing or stale",
                "OpenAI auth bundle is missing or no longer valid; sign in again.",
                true);
        }

        return new WorkflowFailureInfo(
            "reviewer-runtime",
            "Reviewer runtime failed",
            "Reviewer execution failed after the workflow created the progress summary.",
            false);
    }

    private static string ExtractUsageBudgetGuardDetail(string text) {
        var index = text.IndexOf(UsageBudgetGuardPrefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0) {
            return "Usage budget guard blocked this review run.";
        }

        var start = index + UsageBudgetGuardPrefix.Length;
        var end = text.IndexOfAny(['\r', '\n'], start);
        var detail = end < 0 ? text[start..] : text[start..end];
        detail = detail.Trim();
        return detail.Length == 0
            ? "Usage budget guard blocked this review run."
            : detail;
    }

    internal static string BuildWorkflowFailOpenSummaryBody(PullRequestContext context, string reviewerSource,
        string remediationRepo, WorkflowFailureInfo failure) {
        var lines = new List<string> {
            WorkflowSummaryMarker,
            "## IntelligenceX Review (failed open)",
            $"Reviewing this pull request: **{context.Title.Replace("\r", string.Empty).Replace("\n", " ")}**",
            string.Empty,
            "WARNING: Reviewer execution failed and this workflow was allowed to pass open.",
            string.Empty,
            $"- Reviewer source: {reviewerSource}",
            $"- Failure type: {failure.Label}"
        };

        if (!string.IsNullOrWhiteSpace(failure.Detail)) {
            lines.Add($"- Detail: {failure.Detail.Trim()}");
        }

        if (failure.RequiresAuthRemediation) {
            lines.AddRange(new[] {
                string.Empty,
                "> Interactive ChatGPT sign-in cannot run inside GitHub Actions.",
                "> Reauthenticate locally and refresh `INTELLIGENCEX_AUTH_B64` with:",
                $"> `intelligencex auth login --set-github-secret --repo {remediationRepo}`"
            });
        } else {
            lines.AddRange(new[] {
                string.Empty,
                "> Check the `review / review` workflow logs for the runtime failure and rerun the job after fixing the underlying issue."
            });
        }

        lines.AddRange(new[] {
            string.Empty,
            "_The static analysis gate still ran and remained enforcing in this workflow._"
        });

        return string.Join(Environment.NewLine, lines);
    }

    internal static string BuildAuthRemediationCommand(string? repo = null) {
        var resolvedRepo = ResolveAuthRemediationRepo(repo);
        if (!string.IsNullOrWhiteSpace(resolvedRepo)) {
            return $"intelligencex auth login --set-github-secret --repo {QuoteShellArgumentIfNeeded(resolvedRepo)}";
        }
        return "intelligencex auth login";
    }

    private static string QuoteShellArgumentIfNeeded(string value) {
        if (value.IndexOfAny([' ', '\t', '\r', '\n', '"']) < 0) {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    internal static string? ResolveAuthRemediationRepo(string? explicitRepo = null) {
        if (!string.IsNullOrWhiteSpace(explicitRepo)) {
            return explicitRepo.Trim();
        }

        var repo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO");
        if (!string.IsNullOrWhiteSpace(repo)) {
            return repo.Trim();
        }

        repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(repo)) {
            return repo.Trim();
        }

        return null;
    }

    internal static bool IsTrustedSummaryAuthor(string? author) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }

        var normalized = author.Trim();
        if (normalized.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[..^5];
        }
        if (normalized.StartsWith("app/", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[4..];
        }

        return string.Equals(normalized, "intelligencex-review", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "github-actions", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOwnedWorkflowSummaryComment(IssueComment comment) {
        return comment is not null &&
               !string.IsNullOrWhiteSpace(comment.Body) &&
               comment.Body.Contains(WorkflowSummaryMarker, StringComparison.OrdinalIgnoreCase) &&
               IsTrustedSummaryAuthor(comment.Author);
    }

    private static void AppendException(StringBuilder sb, Exception ex, bool includeInner, int depth) {
        if (depth > 0) {
            sb.Append(" | ");
        }
        sb.Append(ex.GetType().Name);
        if (!string.IsNullOrWhiteSpace(ex.Message)) {
            sb.Append(": ").Append(ex.Message);
        }
        if (includeInner && ex.InnerException is not null && depth < 2) {
            AppendException(sb, ex.InnerException, true, depth + 1);
        }
    }

    private static Exception Unwrap(Exception ex) {
        if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1) {
            return Unwrap(aggregate.InnerExceptions[0]);
        }
        return ex.InnerException is not null ? Unwrap(ex.InnerException) : ex;
    }

    private static ReviewErrorInfo ClassifyStatusCode(int code) {
        if (code == (int)HttpStatusCode.Unauthorized || code == (int)HttpStatusCode.Forbidden) {
            return new ReviewErrorInfo(ReviewErrorCategory.Auth, false, $"HTTP {code}");
        }
        if (code == (int)HttpStatusCode.TooManyRequests) {
            return new ReviewErrorInfo(ReviewErrorCategory.RateLimit, true, $"HTTP {code}");
        }
        if (code == (int)HttpStatusCode.RequestTimeout) {
            return new ReviewErrorInfo(ReviewErrorCategory.Timeout, true, $"HTTP {code}");
        }
        if (code >= 500 && code <= 599) {
            return new ReviewErrorInfo(ReviewErrorCategory.ServiceUnavailable, true, $"HTTP {code}");
        }
        if (code == (int)HttpStatusCode.BadRequest || code == (int)HttpStatusCode.UnprocessableEntity) {
            return new ReviewErrorInfo(ReviewErrorCategory.Config, false, $"HTTP {code}");
        }
        return new ReviewErrorInfo(ReviewErrorCategory.Provider, false, $"HTTP {code}");
    }
}
