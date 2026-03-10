using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns TLS/SChannel protocol and crypto posture for the local or remote Windows host.
/// </summary>
public sealed class SystemTlsPostureTool : SystemToolBase, ITool {
    private sealed record TlsPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record TlsPostureResponse(
        string ComputerName,
        bool? FipsEnabled,
        bool? Tls10ServerEnabled,
        bool? Tls11ServerEnabled,
        bool? Tls12ServerEnabled,
        bool? Tls13ServerEnabled,
        int WeakServerProtocolsEnabled,
        int WeakClientProtocolsEnabled,
        int DisabledCipherCount,
        int DisabledHashCount,
        int DisabledKeyExchangeCount,
        int CipherSuiteOrderLength,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_tls_posture",
        "Return TLS/SChannel protocol and crypto posture (protocol roles/ciphers/hashes/FIPS) for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:tls_posture", "intent:schannel_policy", "scope:host_crypto_policy" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTlsPostureTool"/> class.
    /// </summary>
    public SystemTlsPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<TlsPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<TlsPostureRequest>.Success(new TlsPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<TlsPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_tls_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = TlsPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new TlsPostureResponse(
                ComputerName: effectiveComputerName,
                FipsEnabled: posture.FipsEnabled,
                Tls10ServerEnabled: GetServerEnabled(posture, "TLS 1.0"),
                Tls11ServerEnabled: GetServerEnabled(posture, "TLS 1.1"),
                Tls12ServerEnabled: GetServerEnabled(posture, "TLS 1.2"),
                Tls13ServerEnabled: GetServerEnabled(posture, "TLS 1.3"),
                WeakServerProtocolsEnabled: CountWeakEnabled(posture, client: false),
                WeakClientProtocolsEnabled: CountWeakEnabled(posture, client: true),
                DisabledCipherCount: CountDisabled(posture.Ciphers),
                DisabledHashCount: CountDisabled(posture.Hashes),
                DisabledKeyExchangeCount: CountDisabled(posture.KeyExchangeAlgorithms),
                CipherSuiteOrderLength: posture.CipherSuitesOrder?.Length ?? 0,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_tls_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(posture.Protocols.Count + posture.Ciphers.Count + posture.Hashes.Count + posture.KeyExchangeAlgorithms.Count, 1),
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "TLS posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("FIPS Enabled", FormatNullableBool(posture.FipsEnabled)),
                    ("TLS 1.0 Server Enabled", FormatNullableBool(GetServerEnabled(posture, "TLS 1.0"))),
                    ("TLS 1.1 Server Enabled", FormatNullableBool(GetServerEnabled(posture, "TLS 1.1"))),
                    ("TLS 1.2 Server Enabled", FormatNullableBool(GetServerEnabled(posture, "TLS 1.2"))),
                    ("TLS 1.3 Server Enabled", FormatNullableBool(GetServerEnabled(posture, "TLS 1.3"))),
                    ("Weak Server Protocols Enabled", CountWeakEnabled(posture, client: false).ToString(CultureInfo.InvariantCulture)),
                    ("Weak Client Protocols Enabled", CountWeakEnabled(posture, client: true).ToString(CultureInfo.InvariantCulture)),
                    ("Cipher Suite Order Length", (posture.CipherSuitesOrder?.Length ?? 0).ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "TLS posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(TlsPolicyState posture) {
        var warnings = new List<string>();
        if (GetServerEnabled(posture, "SSL 2.0") == true || GetServerEnabled(posture, "SSL 3.0") == true) {
            warnings.Add("Legacy SSL server protocols remain enabled.");
        }
        if (GetServerEnabled(posture, "TLS 1.0") == true || GetServerEnabled(posture, "TLS 1.1") == true) {
            warnings.Add("Legacy TLS server protocols remain enabled.");
        }
        if (GetServerEnabled(posture, "TLS 1.2") == false && GetServerEnabled(posture, "TLS 1.3") == false) {
            warnings.Add("Neither TLS 1.2 nor TLS 1.3 is enabled for the server role.");
        }
        if (posture.FipsEnabled == true) {
            warnings.Add("FIPS algorithm policy is enabled; confirm it is intentional because it can reduce compatibility.");
        }

        return warnings;
    }

    private static bool? GetServerEnabled(TlsPolicyState posture, string protocolName) {
        return posture.Protocols.TryGetValue(protocolName, out var settings)
            ? settings.Server.Enabled
            : null;
    }

    private static int CountWeakEnabled(TlsPolicyState posture, bool client) {
        return posture.Protocols
            .Where(static entry =>
                string.Equals(entry.Key, "SSL 2.0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, "SSL 3.0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, "TLS 1.0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, "TLS 1.1", StringComparison.OrdinalIgnoreCase))
            .Count(entry => client ? entry.Value.Client.Enabled == true : entry.Value.Server.Enabled == true);
    }

    private static int CountDisabled(IReadOnlyDictionary<string, bool?> values) {
        return values.Count(static entry => entry.Value == false);
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
