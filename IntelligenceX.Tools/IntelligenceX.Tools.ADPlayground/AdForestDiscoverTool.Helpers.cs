using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using ADPlayground.Infrastructure;
using ADPlayground.Monitoring.Probes;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdForestDiscoverTool : ActiveDirectoryToolBase, ITool {
    private static async Task CollectDcSourceAsync(
        List<object> perDomainReceipt,
        string sourceName,
        Func<IEnumerable<string>> enumerate,
        HashSet<string> target,
        Func<string, bool> accept,
        int maxCapture,
        int timeoutMs,
        CancellationToken cancellationToken) {
        var step = new DiscoveryStep($"domain_controllers:{sourceName}");
        perDomainReceipt.Add(step);
        step.Start();

        try {
            var raw = await RunWithTimeoutAsync(
                () => EnumerateNormalizedDistinct(enumerate, accept, maxCapture, cancellationToken),
                timeoutMs,
                cancellationToken);
            foreach (var dc in raw) {
                target.Add(dc);
            }

            step.Succeed(new {
                count = raw.Count,
                sample = raw.Take(5).ToArray()
            });
        } catch (Exception ex) {
            step.Fail(ex);
        }
    }

    private static List<string> EnumerateNormalizedDistinct(
        Func<IEnumerable<string>> enumerate,
        Func<string, bool> accept,
        int maxCapture,
        CancellationToken cancellationToken) {
        if (enumerate is null) {
            throw new ArgumentNullException(nameof(enumerate));
        }
        if (accept is null) {
            throw new ArgumentNullException(nameof(accept));
        }
        if (maxCapture < 1) {
            return new List<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dc in enumerate()) {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(dc)) {
                continue;
            }
            var normalized = NormalizeHostOrName(dc);
            if (string.IsNullOrWhiteSpace(normalized)) {
                continue;
            }
            if (!accept(normalized)) {
                continue;
            }
            set.Add(normalized);
            if (set.Count >= maxCapture) {
                break;
            }
        }

        return set
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<T> func,
        int timeoutMs,
        CancellationToken cancellationToken) {
        if (func is null) {
            throw new ArgumentNullException(nameof(func));
        }
        if (timeoutMs <= 0) {
            return func();
        }

        var work = Task.Run(func);
        var completed = await Task.WhenAny(work, Task.Delay(timeoutMs, cancellationToken));
        if (completed != work) {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Operation exceeded timeout ({timeoutMs}ms).");
        }

        return await work;
    }

    private static HashSet<string> BuildSet(IEnumerable<string>? items) {
        if (items is null) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = items
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => NormalizeHostOrName(x))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return normalized;
    }

    private static bool LooksLikeRodc(string host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return false;
        }

        var normalized = NormalizeHostOrName(host);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return false;
        }

        int separator = normalized.IndexOf('.');
        var label = separator >= 0 ? normalized.Substring(0, separator) : normalized;
        if (string.IsNullOrWhiteSpace(label)) {
            return false;
        }

        return label.StartsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
               label.EndsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("-rodc", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("rodc-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRodcBestEffort(string host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return false;
        }

        try {
            return DomainHelper.IsReadOnlyDc(NormalizeHostOrName(host));
        } catch {
            return LooksLikeRodc(host);
        }
    }

    private static object MapTrust(System.DirectoryServices.ActiveDirectory.TrustRelationshipInformation trust, string scope) {
        if (trust is null) {
            return new { scope, source_name = string.Empty, target_name = string.Empty, trust_type = string.Empty, trust_direction = string.Empty };
        }

        string source = string.Empty;
        string target = string.Empty;
        string type = string.Empty;
        string direction = string.Empty;

        try { source = trust.SourceName ?? string.Empty; } catch { }
        try { target = trust.TargetName ?? string.Empty; } catch { }
        try { type = trust.TrustType.ToString(); } catch { }
        try { direction = trust.TrustDirection.ToString(); } catch { }

        return new {
            scope,
            source_name = source,
            target_name = target,
            trust_type = type,
            trust_direction = direction
        };
    }

    private static string? NormalizeOptional(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var trimmed = value!.Trim().TrimEnd('.');
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeHostOrName(string input) {
        return (input ?? string.Empty).Trim().TrimEnd('.');
    }

    private static string ToDiscoveryFallbackName(DirectoryDiscoveryFallback fallback) {
        return fallback switch {
            DirectoryDiscoveryFallback.None => "none",
            DirectoryDiscoveryFallback.CurrentForest => "current_forest",
            _ => "current_domain"
        };
    }

    private sealed class DiscoveryStep {
        private readonly Stopwatch _sw = new();

        public DiscoveryStep(string name) {
            Name = name;
        }

        public string Name { get; }

        public void Start() {
            _sw.Restart();
        }

        public void Succeed(object? output = null) {
            _sw.Stop();
            Ok = true;
            DurationMs = (int)Math.Min(int.MaxValue, _sw.Elapsed.TotalMilliseconds);
            Output = output;
        }

        public void Fail(Exception ex) {
            _sw.Stop();
            Ok = false;
            DurationMs = (int)Math.Min(int.MaxValue, _sw.Elapsed.TotalMilliseconds);
            Error = ex.Message;
            ErrorType = ex.GetType().FullName ?? "Exception";
        }

        public bool Ok { get; private set; }
        public int DurationMs { get; private set; }
        public object? Output { get; private set; }
        public string? Error { get; private set; }
        public string? ErrorType { get; private set; }
    }
}
