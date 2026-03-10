using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Credentials;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns credential-related security posture for the local or remote Windows host.
/// </summary>
public sealed class SystemCredentialPostureTool : SystemToolBase, ITool {
    private sealed record CredentialPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeStoredCredentials,
        int MaxStoredCredentials);

    private sealed record CredentialPostureResponse(
        string ComputerName,
        int? CachedLogonsCount,
        bool? WDigestUseLogonCredential,
        bool? NoLmHash,
        bool? CredentialGuardConfigured,
        bool? RdpNlaRequired,
        bool StoredCredentialsQueried,
        IReadOnlyList<StoredCredentialInfo> StoredCredentials);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_credential_posture",
        "Return credential-related security posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_stored_credentials", ToolSchema.Boolean("When true, include capped local stored-credential rows when available. Default false.")),
                ("max_stored_credentials", ToolSchema.Integer("Optional maximum stored-credential rows when include_stored_credentials=true (capped). Default 25.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemCredentialPostureTool"/> class.
    /// </summary>
    public SystemCredentialPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<CredentialPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<CredentialPostureRequest>.Success(new CredentialPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeStoredCredentials: reader.Boolean("include_stored_credentials", defaultValue: false),
                MaxStoredCredentials: ToolArgs.GetCappedInt32(arguments, "max_stored_credentials", 25, 1, 250)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<CredentialPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_credential_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await CredentialPosture
                .GetAsync(request.ComputerName, cancellationToken)
                .ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var storedCredentials = request.IncludeStoredCredentials
                ? CapRows(posture.StoredCredentials, request.MaxStoredCredentials, out _, out _)
                : Array.Empty<StoredCredentialInfo>();
            var model = new CredentialPostureResponse(
                ComputerName: effectiveComputerName,
                CachedLogonsCount: posture.CachedLogonsCount,
                WDigestUseLogonCredential: posture.WDigestUseLogonCredential,
                NoLmHash: posture.NoLmHash,
                CredentialGuardConfigured: posture.CredentialGuardConfigured,
                RdpNlaRequired: posture.RdpNlaRequired,
                StoredCredentialsQueried: posture.StoredCredentialsQueried,
                StoredCredentials: storedCredentials);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName, mutate: x => {
                x.Add("include_stored_credentials", request.IncludeStoredCredentials);
                if (request.IncludeStoredCredentials) {
                    x.Add("max_stored_credentials", request.MaxStoredCredentials);
                }
                x.Add("stored_credentials_queried", posture.StoredCredentialsQueried);
            });
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_credential_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Credential posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Cached Logons Count", posture.CachedLogonsCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("WDigest UseLogonCredential", FormatNullableBool(posture.WDigestUseLogonCredential)),
                    ("NoLMHash", FormatNullableBool(posture.NoLmHash)),
                    ("Credential Guard Configured", FormatNullableBool(posture.CredentialGuardConfigured)),
                    ("RDP NLA Required", FormatNullableBool(posture.RdpNlaRequired)),
                    ("Stored Credentials Queried", posture.StoredCredentialsQueried ? "true" : "false"),
                    ("Stored Credentials Returned", storedCredentials.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Credential posture query failed.");
        }
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
