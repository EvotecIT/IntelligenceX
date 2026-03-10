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
/// Returns TLS/SChannel posture details from ComputerX registry-backed policy state (read-only).
/// </summary>
public sealed class SystemTlsPostureTool : SystemToolBase, ITool {
    private sealed record TlsPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeAlgorithms,
        bool IncludeCipherSuitesOrder);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_tls_posture",
        "Return TLS/SChannel security posture (protocols, FIPS, cipher suite order, optional algorithms) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_algorithms", ToolSchema.Boolean("When true, include hashes, ciphers, and key-exchange algorithm maps.")),
                ("include_cipher_suites_order", ToolSchema.Boolean("When true (default), include configured cipher suite order when available.")))
            .NoAdditionalProperties());

    private sealed record SystemTlsPostureResult(
        string ComputerName,
        TlsPolicyState Policy,
        int WeakServerProtocolsEnabled,
        int WeakClientProtocolsEnabled,
        bool HasCipherSuiteOrder,
        IReadOnlyList<string> Warnings);

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
                Target: ResolveTargetComputerName(computerName),
                IncludeAlgorithms: reader.Boolean("include_algorithms", defaultValue: false),
                IncludeCipherSuitesOrder: reader.Boolean("include_cipher_suites_order", defaultValue: true)));
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
            var raw = TlsPolicyQuery.Get(request.ComputerName);
            var policy = new TlsPolicyState {
                Protocols = raw.Protocols,
                Hashes = request.IncludeAlgorithms ? raw.Hashes : new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                Ciphers = request.IncludeAlgorithms ? raw.Ciphers : new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                KeyExchangeAlgorithms = request.IncludeAlgorithms ? raw.KeyExchangeAlgorithms : new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                CipherSuitesOrder = request.IncludeCipherSuitesOrder ? raw.CipherSuitesOrder : null,
                FipsEnabled = raw.FipsEnabled
            };

            var weakServerProtocolsEnabled = CountExplicitlyEnabledWeakProtocols(raw, server: true);
            var weakClientProtocolsEnabled = CountExplicitlyEnabledWeakProtocols(raw, server: false);
            var hasCipherSuiteOrder = raw.CipherSuitesOrder is { Length: > 0 };

            var warnings = new List<string>();
            if (weakServerProtocolsEnabled > 0) {
                warnings.Add("Weak server TLS protocols are explicitly enabled.");
            }
            if (weakClientProtocolsEnabled > 0) {
                warnings.Add("Weak client TLS protocols are explicitly enabled.");
            }
            if (!hasCipherSuiteOrder) {
                warnings.Add("Cipher suite order is not explicitly configured.");
            }

            var model = new SystemTlsPostureResult(
                ComputerName: request.Target,
                Policy: policy,
                WeakServerProtocolsEnabled: weakServerProtocolsEnabled,
                WeakClientProtocolsEnabled: weakClientProtocolsEnabled,
                HasCipherSuiteOrder: hasCipherSuiteOrder,
                Warnings: warnings);

            return Task.FromResult(ToolResultV2.OkFactsModelWithRenderValue(
                model: model,
                title: "System TLS posture",
                facts: new[] {
                    ("Computer", request.Target),
                    ("WeakServerProtocolsEnabled", weakServerProtocolsEnabled.ToString()),
                    ("WeakClientProtocolsEnabled", weakClientProtocolsEnabled.ToString()),
                    ("CipherSuiteOrderConfigured", hasCipherSuiteOrder.ToString()),
                    ("FipsEnabled", raw.FipsEnabled?.ToString() ?? string.Empty),
                    ("Warnings", warnings.Count.ToString())
                },
                meta: BuildFactsMeta(
                    count: warnings.Count,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => {
                        meta.Add("include_algorithms", request.IncludeAlgorithms);
                        meta.Add("include_cipher_suites_order", request.IncludeCipherSuitesOrder);
                    }),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: SystemRenderHintBuilders.BuildWarningListHints(warnings.Count)));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "TLS posture query failed."));
        }
    }

    private static int CountExplicitlyEnabledWeakProtocols(TlsPolicyState state, bool server) {
        var weakProtocols = new[] { "SSL 2.0", "SSL 3.0", "TLS 1.0", "TLS 1.1" };
        var enabled = 0;
        foreach (var protocolName in weakProtocols) {
            if (!state.Protocols.TryGetValue(protocolName, out var settings) || settings is null) {
                continue;
            }

            var role = server ? settings.Server : settings.Client;
            if (role.Enabled == true) {
                enabled++;
            }
        }

        return enabled;
    }
}
