using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns network client hardening posture for the local or remote Windows host.
/// </summary>
public sealed class SystemNetworkClientPostureTool : SystemToolBase, ITool {
    private sealed record NetworkClientPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record NetworkClientPostureResponse(
        string ComputerName,
        bool? LlmnrEnableMulticast,
        bool? MdnsEnabled,
        bool? Ipv4IcmpRedirectsEnabled,
        int? Ipv4DisableIpSourceRouting,
        bool? Ipv4PerformRouterDiscovery,
        bool? Ipv6IcmpRedirectsEnabled,
        int? Ipv6DisableIpSourceRouting,
        int? NoDriveTypeAutoRun,
        bool? NoAutoRun,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_network_client_posture",
        "Return LLMNR, mDNS, ICMP redirect, source-routing, and AutoRun posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:network_client_posture", "intent:name_resolution_policy", "scope:host_network_client_policy" });

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
            var posture = NetworkClientPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new NetworkClientPostureResponse(
                ComputerName: effectiveComputerName,
                LlmnrEnableMulticast: posture.LlmnrEnableMulticast,
                MdnsEnabled: posture.MdnsEnabled,
                Ipv4IcmpRedirectsEnabled: posture.Ipv4IcmpRedirectsEnabled,
                Ipv4DisableIpSourceRouting: posture.Ipv4DisableIPSourceRouting,
                Ipv4PerformRouterDiscovery: posture.Ipv4PerformRouterDiscovery,
                Ipv6IcmpRedirectsEnabled: posture.Ipv6IcmpRedirectsEnabled,
                Ipv6DisableIpSourceRouting: posture.Ipv6DisableIPSourceRouting,
                NoDriveTypeAutoRun: posture.NoDriveTypeAutoRun,
                NoAutoRun: posture.NoAutoRun,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_network_client_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Network client posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("LLMNR Enabled", FormatNullableBool(posture.LlmnrEnableMulticast)),
                    ("mDNS Enabled", FormatNullableBool(posture.MdnsEnabled)),
                    ("IPv4 ICMP Redirects Enabled", FormatNullableBool(posture.Ipv4IcmpRedirectsEnabled)),
                    ("IPv4 Disable Source Routing", posture.Ipv4DisableIPSourceRouting?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("IPv6 ICMP Redirects Enabled", FormatNullableBool(posture.Ipv6IcmpRedirectsEnabled)),
                    ("IPv6 Disable Source Routing", posture.Ipv6DisableIPSourceRouting?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("No AutoRun", FormatNullableBool(posture.NoAutoRun)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Network client posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(NetworkClientPolicyState posture) {
        var warnings = new List<string>();
        if (posture.LlmnrEnableMulticast == true) {
            warnings.Add("LLMNR multicast name resolution is enabled.");
        }
        if (posture.MdnsEnabled == true) {
            warnings.Add("mDNS name resolution is enabled.");
        }
        if (posture.Ipv4IcmpRedirectsEnabled == true || posture.Ipv6IcmpRedirectsEnabled == true) {
            warnings.Add("ICMP redirects are enabled for IPv4 or IPv6.");
        }
        if ((posture.Ipv4DisableIPSourceRouting.HasValue && posture.Ipv4DisableIPSourceRouting.Value < 2)
            || (posture.Ipv6DisableIPSourceRouting.HasValue && posture.Ipv6DisableIPSourceRouting.Value < 2)) {
            warnings.Add("IP source routing is not fully disabled.");
        }
        if (posture.NoAutoRun == false) {
            warnings.Add("AutoRun is not disabled.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
