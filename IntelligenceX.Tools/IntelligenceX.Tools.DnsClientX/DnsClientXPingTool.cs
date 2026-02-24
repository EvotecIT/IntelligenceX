using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// Performs bounded ICMP ping checks for one or more targets.
/// </summary>
public sealed class DnsClientXPingTool : DnsClientXToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "dnsclientx_ping",
        "Perform quick ICMP reachability checks for one or more targets (read-only).",
        ToolSchema.Object(
                ("target", ToolSchema.String("Single target host/IP to ping. Alternative to targets[].")),
                ("targets", ToolSchema.Array(ToolSchema.String(), "Target hosts/IPs to ping (deduplicated and capped).")),
                ("timeout_ms", ToolSchema.Integer("Per-target timeout in milliseconds (capped by pack options).")),
                ("max_targets", ToolSchema.Integer("Maximum targets to evaluate from input (capped by pack options).")),
                ("dont_fragment", ToolSchema.Boolean("Set ICMP Don't Fragment flag (default: false).")),
                ("buffer_size", ToolSchema.Integer("Ping payload size in bytes (default: 32, capped at 1024).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClientXPingTool"/> class.
    /// </summary>
    public DnsClientXPingTool(DnsClientXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var targets = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("targets"));
        var singleTarget = ToolArgs.GetOptionalTrimmed(arguments, "target");
        if (!string.IsNullOrWhiteSpace(singleTarget) && !targets.Contains(singleTarget, StringComparer.OrdinalIgnoreCase)) {
            targets.Insert(0, singleTarget);
        }

        if (targets.Count == 0) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "Provide target or targets for ping checks.",
                hints: new[] { "Example: target='dc01.contoso.local' or targets=['dc01','dc02']." },
                isTransient: false);
        }

        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", Options.DefaultPingTimeoutMs, 100, Options.MaxPingTimeoutMs);
        var maxTargets = ToolArgs.GetCappedInt32(arguments, "max_targets", Options.MaxPingTargets, 1, Options.MaxPingTargets);
        var dontFragment = ToolArgs.GetBoolean(arguments, "dont_fragment", defaultValue: false);
        var bufferSize = ToolArgs.GetCappedInt32(arguments, "buffer_size", defaultValue: 32, minInclusive: 1, maxInclusive: 1024);

        var selectedTargets = targets.Take(maxTargets).ToArray();
        var truncated = targets.Count > selectedTargets.Length;
        var warnings = new List<string>();
        if (truncated) {
            warnings.Add($"Input targets were capped to {selectedTargets.Length}.");
        }

        var probes = new List<DnsClientXPingProbeModel>(selectedTargets.Length);
        var buffer = new byte[bufferSize];
        for (var i = 0; i < selectedTargets.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();

            var target = selectedTargets[i];
            try {
                using var ping = new Ping();
                var options = new PingOptions(ttl: 64, dontFragment: dontFragment);
                var reply = await ping.SendPingAsync(target, timeoutMs, buffer, options);

                probes.Add(new DnsClientXPingProbeModel {
                    Target = target,
                    Status = reply.Status.ToString(),
                    Address = reply.Address?.ToString(),
                    RoundTripMilliseconds = reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                    Ttl = reply.Options?.Ttl,
                    BufferSize = bufferSize,
                    Error = null
                });
            } catch (Exception ex) when (ex is PingException or InvalidOperationException or ArgumentException or NotSupportedException) {
                probes.Add(new DnsClientXPingProbeModel {
                    Target = target,
                    Status = "Error",
                    Address = null,
                    RoundTripMilliseconds = null,
                    Ttl = null,
                    BufferSize = bufferSize,
                    Error = ex.Message
                });
            }
        }

        var successCount = probes.Count(static probe => string.Equals(probe.Status, IPStatus.Success.ToString(), StringComparison.OrdinalIgnoreCase));
        var failedCount = probes.Count - successCount;

        var result = new DnsClientXPingResultModel {
            TimeoutMs = timeoutMs,
            DontFragment = dontFragment,
            BufferSize = bufferSize,
            RequestedTargets = targets,
            ProbedTargets = selectedTargets,
            SuccessCount = successCount,
            FailedCount = failedCount,
            Truncated = truncated,
            Results = probes,
            Warnings = warnings
        };

        var summary = ToolMarkdown.SummaryFacts(
            title: "DnsClientX ping",
            facts: new[] {
                ("Targets", result.ProbedTargets.Count.ToString(CultureInfo.InvariantCulture)),
                ("Success", successCount.ToString(CultureInfo.InvariantCulture)),
                ("Failed", failedCount.ToString(CultureInfo.InvariantCulture)),
                ("Timeout (ms)", timeoutMs.ToString(CultureInfo.InvariantCulture))
            });

        var meta = ToolOutputHints.Meta(count: probes.Count, truncated: truncated)
            .Add("success_count", successCount)
            .Add("failed_count", failedCount)
            .Add("timeout_ms", timeoutMs);

        return ToolResponse.OkModel(result, meta: meta, summaryMarkdown: summary);
    }

    private sealed class DnsClientXPingResultModel {
        public int TimeoutMs { get; init; }
        public bool DontFragment { get; init; }
        public int BufferSize { get; init; }
        public IReadOnlyList<string> RequestedTargets { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ProbedTargets { get; init; } = Array.Empty<string>();
        public int SuccessCount { get; init; }
        public int FailedCount { get; init; }
        public bool Truncated { get; init; }
        public IReadOnlyList<DnsClientXPingProbeModel> Results { get; init; } = Array.Empty<DnsClientXPingProbeModel>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    private sealed class DnsClientXPingProbeModel {
        public string Target { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Address { get; init; }
        public long? RoundTripMilliseconds { get; init; }
        public int? Ttl { get; init; }
        public int BufferSize { get; init; }
        public string? Error { get; init; }
    }
}

