using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

#if DOMAINDETECTIVE_ENABLED
using System.Net.NetworkInformation;
using DomainDetective.Network;
#endif

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Runs bounded network diagnostics (ping/traceroute) via DomainDetective.
/// </summary>
public sealed class DomainDetectiveNetworkProbeTool : DomainDetectiveToolBase, ITool {
    private const int DefaultNetworkTimeoutMs = 4000;
    private const int MaxNetworkTimeoutMs = 15000;
    private const int DefaultTracerouteMaxHops = 16;
    private const int MaxTracerouteMaxHops = 30;

    private static readonly ToolDefinition DefinitionValue = new(
        "domaindetective_network_probe",
        "Run DomainDetective network diagnostics (ping/traceroute) for a host (read-only).",
        ToolSchema.Object(
                ("host", ToolSchema.String("Target host or IP address to probe.")),
                ("run_ping", ToolSchema.Boolean("Run ICMP ping probe (default: true).")),
                ("run_traceroute", ToolSchema.Boolean("Run traceroute probe (default: false).")),
                ("timeout_ms", ToolSchema.Integer("Per-probe timeout in milliseconds (capped).")),
                ("max_hops", ToolSchema.Integer("Maximum traceroute hops when run_traceroute=true (capped).")))
            .Required("host")
            .NoAdditionalProperties(),
        category: "dns",
        tags: new[] {
            "reachability",
            "dns"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectiveNetworkProbeTool"/> class.
    /// </summary>
    public DomainDetectiveNetworkProbeTool(DomainDetectiveToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var host = ToolArgs.GetOptionalTrimmed(arguments, "host");
        if (string.IsNullOrWhiteSpace(host)) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "host is required.",
                hints: new[] { "Provide a host name or IP address, for example: dc01.contoso.local or 1.1.1.1." },
                isTransient: false);
        }

        var runPing = ToolArgs.GetBoolean(arguments, "run_ping", defaultValue: true);
        var runTraceroute = ToolArgs.GetBoolean(arguments, "run_traceroute", defaultValue: false);
        if (!runPing && !runTraceroute) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "At least one probe must be enabled (run_ping or run_traceroute).",
                hints: new[] { "Set run_ping=true and/or run_traceroute=true." },
                isTransient: false);
        }

        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", DefaultNetworkTimeoutMs, 100, MaxNetworkTimeoutMs);
        var maxHops = ToolArgs.GetCappedInt32(arguments, "max_hops", DefaultTracerouteMaxHops, 1, MaxTracerouteMaxHops);

        var warnings = new List<string>();
        var requestedTimeout = arguments?.GetInt64("timeout_ms");
        if (requestedTimeout.HasValue && requestedTimeout.Value > MaxNetworkTimeoutMs) {
            warnings.Add($"timeout_ms was capped to {MaxNetworkTimeoutMs}.");
        }
        var requestedMaxHops = arguments?.GetInt64("max_hops");
        if (requestedMaxHops.HasValue && requestedMaxHops.Value > MaxTracerouteMaxHops) {
            warnings.Add($"max_hops was capped to {MaxTracerouteMaxHops}.");
        }

#if !DOMAINDETECTIVE_ENABLED
        return ToolResponse.Error(
            errorCode: "dependency_unavailable",
            error: "DomainDetective dependency is not available in this build.",
            hints: new[] {
                "Provide DomainDetective as a sibling source checkout or package reference.",
                "Disable the domaindetective pack when running in builds without the dependency."
            },
            isTransient: false);
#else
        DomainDetectivePingResultModel? ping = null;
        string? pingError = null;
        if (runPing) {
            try {
                var reply = await PingTraceroute.PingAsync(host, timeoutMs).ConfigureAwait(false);
                ping = new DomainDetectivePingResultModel {
                    Status = reply.Status.ToString(),
                    Address = reply.Address?.ToString(),
                    RoundTripMilliseconds = reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                    TimeToLive = reply.Options?.Ttl
                };
            } catch (Exception ex) when (ex is PingException or InvalidOperationException or ArgumentException or NotSupportedException) {
                pingError = ex.Message;
            }
        }

        IReadOnlyList<DomainDetectiveTracerouteHopModel> traceroute = Array.Empty<DomainDetectiveTracerouteHopModel>();
        string? tracerouteError = null;
        var tracerouteCompleted = false;
        if (runTraceroute) {
            try {
                var hops = await PingTraceroute.TracerouteAsync(host, maxHops, timeoutMs).ConfigureAwait(false);
                traceroute = hops
                    .Select(static hop => new DomainDetectiveTracerouteHopModel {
                        Hop = hop.Hop,
                        Address = hop.Address,
                        Status = hop.Status.ToString(),
                        RoundTripMilliseconds = hop.RoundtripTime
                    })
                    .ToArray();
                tracerouteCompleted = traceroute.LastOrDefault()?.Status?.Equals(IPStatus.Success.ToString(), StringComparison.OrdinalIgnoreCase) == true;
            } catch (Exception ex) when (ex is PingException or InvalidOperationException or ArgumentException or NotSupportedException) {
                tracerouteError = ex.Message;
            }
        }

        var allRequestedProbesFailed =
            (!runPing || ping is null)
            && (!runTraceroute || traceroute.Count == 0)
            && (!string.IsNullOrWhiteSpace(pingError) || !string.IsNullOrWhiteSpace(tracerouteError));
        if (allRequestedProbesFailed) {
            var combinedError = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(pingError)) {
                combinedError.Add("ping: " + pingError.Trim());
            }
            if (!string.IsNullOrWhiteSpace(tracerouteError)) {
                combinedError.Add("traceroute: " + tracerouteError.Trim());
            }

            return ToolResponse.Error(
                errorCode: "probe_failed",
                error: "Network probe failed: " + string.Join("; ", combinedError),
                hints: new[] {
                    "Verify host reachability and local ICMP permissions.",
                    "Retry with a higher timeout_ms for slow paths."
                },
                isTransient: true);
        }

        var result = new DomainDetectiveNetworkProbeResultModel {
            Host = host,
            RunPing = runPing,
            RunTraceroute = runTraceroute,
            TimeoutMs = timeoutMs,
            MaxHops = maxHops,
            Ping = ping,
            PingError = pingError,
            Traceroute = traceroute,
            TracerouteError = tracerouteError,
            TracerouteCompleted = tracerouteCompleted,
            Warnings = warnings
        };

        var successCount = 0;
        if (ping is not null && string.Equals(ping.Status, IPStatus.Success.ToString(), StringComparison.OrdinalIgnoreCase)) {
            successCount++;
        }
        if (runTraceroute && tracerouteCompleted) {
            successCount++;
        }

        var summary = ToolMarkdown.SummaryFacts(
            title: "DomainDetective network probe",
            facts: new[] {
                ("Host", result.Host),
                ("Ping", result.Ping?.Status ?? (runPing ? "error" : "skipped")),
                ("Traceroute hops", runTraceroute ? result.Traceroute.Count.ToString(CultureInfo.InvariantCulture) : "skipped"),
                ("Traceroute complete", runTraceroute ? (result.TracerouteCompleted ? "yes" : "no") : "skipped"),
                ("Timeout (ms)", timeoutMs.ToString(CultureInfo.InvariantCulture))
            });

        var meta = ToolOutputHints.Meta(count: Math.Max(1, traceroute.Count), truncated: false)
            .Add("host", result.Host)
            .Add("success_count", successCount)
            .Add("run_ping", result.RunPing)
            .Add("run_traceroute", result.RunTraceroute)
            .Add("traceroute_hops", result.Traceroute.Count);

        return ToolResponse.OkModel(result, meta: meta, summaryMarkdown: summary);
#endif
    }

    private sealed class DomainDetectiveNetworkProbeResultModel {
        public string Host { get; init; } = string.Empty;
        public bool RunPing { get; init; }
        public bool RunTraceroute { get; init; }
        public int TimeoutMs { get; init; }
        public int MaxHops { get; init; }
        public DomainDetectivePingResultModel? Ping { get; init; }
        public string? PingError { get; init; }
        public IReadOnlyList<DomainDetectiveTracerouteHopModel> Traceroute { get; init; } = Array.Empty<DomainDetectiveTracerouteHopModel>();
        public string? TracerouteError { get; init; }
        public bool TracerouteCompleted { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    private sealed class DomainDetectivePingResultModel {
        public string Status { get; init; } = string.Empty;
        public string? Address { get; init; }
        public long? RoundTripMilliseconds { get; init; }
        public int? TimeToLive { get; init; }
    }

    private sealed class DomainDetectiveTracerouteHopModel {
        public int Hop { get; init; }
        public string? Address { get; init; }
        public string Status { get; init; } = string.Empty;
        public long RoundTripMilliseconds { get; init; }
    }
}
