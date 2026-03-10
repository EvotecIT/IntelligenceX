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
/// Returns LDAP signing and channel-binding policy posture for the local or remote Windows host.
/// </summary>
public sealed class SystemLdapPolicyPostureTool : SystemToolBase, ITool {
    private sealed record LdapPolicyPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record LdapPolicyPostureResponse(
        string ComputerName,
        LdapClientPolicyState ClientPolicy,
        LdapServerPolicyState ServerPolicy,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_ldap_policy_posture",
        "Return LDAP client/server signing and channel-binding policy posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:ldap_policy_posture", "intent:ldap_signing_policy", "scope:host_ldap_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemLdapPolicyPostureTool"/> class.
    /// </summary>
    public SystemLdapPolicyPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<LdapPolicyPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<LdapPolicyPostureRequest>.Success(new LdapPolicyPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<LdapPolicyPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_ldap_policy_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var clientPolicy = LdapPolicyQuery.GetClient(request.ComputerName);
            var serverPolicy = LdapPolicyQuery.GetServer(request.ComputerName);
            var warnings = BuildWarnings(clientPolicy, serverPolicy);
            var effectiveComputerName = request.Target;
            var model = new LdapPolicyPostureResponse(
                ComputerName: effectiveComputerName,
                ClientPolicy: clientPolicy,
                ServerPolicy: serverPolicy,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_ldap_policy_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 2,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "LDAP policy posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Client Integrity", clientPolicy.LdapClientIntegrity?.ToString() ?? "unknown"),
                    ("Server Integrity", serverPolicy.LdapServerIntegrity?.ToString() ?? "unknown"),
                    ("Channel Binding", serverPolicy.LdapEnforceChannelBinding?.ToString() ?? "unknown"),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "LDAP policy posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(LdapClientPolicyState clientPolicy, LdapServerPolicyState serverPolicy) {
        var warnings = new List<string>();
        if (clientPolicy.LdapClientIntegrity == LdapIntegrityLevel.None) {
            warnings.Add("LDAP client signing is not required.");
        }
        if (serverPolicy.LdapServerIntegrity == LdapIntegrityLevel.None) {
            warnings.Add("LDAP server signing is not required.");
        }
        if (serverPolicy.LdapEnforceChannelBinding == LdapChannelBindingMode.Disabled) {
            warnings.Add("LDAP channel binding is disabled on the server policy path.");
        }

        return warnings;
    }
}
