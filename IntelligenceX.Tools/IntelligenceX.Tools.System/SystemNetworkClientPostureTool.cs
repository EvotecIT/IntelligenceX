using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns network client hardening posture from ComputerX registry-backed policy state (read-only).
/// </summary>
public sealed class SystemNetworkClientPostureTool : SystemToolBase, ITool {
    private sealed record NetworkClientPostureRequest(
        string? ComputerName,
        string Target);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_network_client_posture",
        "Return network client hardening posture (LLMNR, mDNS, ICMP redirects, source routing, AutoRun) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    private sealed record SystemNetworkClientPostureResult(
        string ComputerName,
        NetworkClientPolicyState Policy,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemNetworkClientPostureTool"/> class.
    /// </summary>
    public SystemNetworkClientPostureTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<NetworkClientPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<NetworkClientPostureRequest>.Success(new NetworkClientPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<NetworkClientPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_network_client_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;

        try {
            var policy = NetworkClientPolicyQuery.Get(request.ComputerName);
            var warnings = new List<string>();

            if (policy.LlmnrEnableMulticast != false) {
                warnings.Add("LLMNR multicast resolution is not explicitly disabled.");
            }
            if (policy.MdnsEnabled != false) {
                warnings.Add("mDNS name resolution is not explicitly disabled.");
            }
            if (policy.Ipv4IcmpRedirectsEnabled == true || policy.Ipv6IcmpRedirectsEnabled == true) {
                warnings.Add("ICMP redirects are enabled for IPv4 or IPv6.");
            }
            if (!policy.Ipv4DisableIPSourceRouting.HasValue || policy.Ipv4DisableIPSourceRouting.Value < 2) {
                warnings.Add("IPv4 source routing is not fully disabled.");
            }
            if (!policy.Ipv6DisableIPSourceRouting.HasValue || policy.Ipv6DisableIPSourceRouting.Value < 2) {
                warnings.Add("IPv6 source routing is not fully disabled.");
            }
            if (policy.NoAutoRun != true) {
                warnings.Add("AutoRun is not explicitly disabled.");
            }

            var model = new SystemNetworkClientPostureResult(
                ComputerName: request.Target,
                Policy: policy,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System network client posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("LlmnrEnableMulticast", policy.LlmnrEnableMulticast?.ToString() ?? string.Empty),
                    ("MdnsEnabled", policy.MdnsEnabled?.ToString() ?? string.Empty),
                    ("Ipv4IcmpRedirectsEnabled", policy.Ipv4IcmpRedirectsEnabled?.ToString() ?? string.Empty),
                    ("Ipv6IcmpRedirectsEnabled", policy.Ipv6IcmpRedirectsEnabled?.ToString() ?? string.Empty),
                    ("NoAutoRun", policy.NoAutoRun?.ToString() ?? string.Empty),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Network client posture query failed."));
        }
    }
}
