using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.Telemetry;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewDiagnosticsSnapshot {
    public ReviewDiagnosticsSnapshot(IReadOnlyList<string> standardError, Exception? lastRpcError, string? lastRpcMethod,
        TimeSpan? lastRpcDuration) {
        StandardError = standardError;
        LastRpcError = lastRpcError;
        LastRpcMethod = lastRpcMethod;
        LastRpcDuration = lastRpcDuration;
    }

    public IReadOnlyList<string> StandardError { get; }
    public Exception? LastRpcError { get; }
    public string? LastRpcMethod { get; }
    public TimeSpan? LastRpcDuration { get; }
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
            return new ReviewDiagnosticsSnapshot(new List<string>(_stderr), _lastRpcError, _lastRpcMethod, _lastRpcDuration);
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

    public static string FormatExceptionSummary(Exception ex, bool includeInner) {
        var sb = new StringBuilder();
        AppendException(sb, ex, includeInner, 0);
        return sb.ToString();
    }

    public static string BuildFailureBody(Exception ex, ReviewSettings settings, ReviewDiagnosticsSnapshot? snapshot) {
        var summary = FormatExceptionSummary(ex, settings.Diagnostics);
        var sb = new StringBuilder();
        sb.AppendLine(FailureMarker);
        sb.AppendLine("WARNING: Review failed to complete due to an OpenAI request error.");
        sb.AppendLine();
        sb.AppendLine($"- Transport: {settings.OpenAITransport}");
        sb.AppendLine($"- Model: {settings.Model}");
        if (!string.IsNullOrWhiteSpace(summary)) {
            sb.AppendLine($"- Error: {summary}");
        }
        if (settings.Diagnostics && snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.LastRpcMethod)) {
            sb.AppendLine($"- Last RPC: {snapshot.LastRpcMethod}");
        }
        sb.AppendLine();
        sb.AppendLine("_Re-run the workflow once connectivity is restored. Set `REVIEW_FAIL_OPEN=false` to keep failures blocking._");
        return sb.ToString().TrimEnd();
    }

    public static void LogFailure(Exception ex, ReviewSettings settings, ReviewDiagnosticsSnapshot? snapshot) {
        Console.Error.WriteLine("OpenAI request failed.");
        Console.Error.WriteLine($"Transport: {settings.OpenAITransport} | Model: {settings.Model}");
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
            }
        }

        if (!settings.Diagnostics || snapshot is null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastRpcMethod)) {
            var rpcSummary = snapshot.LastRpcDuration.HasValue
                ? $"{snapshot.LastRpcMethod} ({snapshot.LastRpcDuration.Value.TotalMilliseconds:0} ms)"
                : snapshot.LastRpcMethod;
            Console.Error.WriteLine($"Last RPC: {rpcSummary}");
        }
        if (snapshot.LastRpcError is not null) {
            Console.Error.WriteLine($"RPC error: {FormatExceptionSummary(snapshot.LastRpcError, true)}");
        }
        if (snapshot.StandardError.Count > 0) {
            Console.Error.WriteLine("App-server stderr (most recent first):");
            for (var i = snapshot.StandardError.Count - 1; i >= 0; i--) {
                Console.Error.WriteLine($"  {snapshot.StandardError[i]}");
            }
        }
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
}
