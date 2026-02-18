using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Ldap;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Diagnostics for LDAP/LDAPS/GC connectivity, binding, and certificate metadata (read-only).
/// </summary>
public sealed class AdLdapDiagnosticsTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultTimeoutMs = 5_000;
    private const int MaxTimeoutMs = 60_000;
    private const int DefaultMaxServers = 10;
    private const int MaxServersCap = 50;
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ldap_diagnostics",
        "Test LDAP/LDAPS/GC endpoints (ports, bind, and LDAPS certificate metadata) for one or more domain controllers (read-only).",
        ToolSchema.Object(
                ("servers", ToolSchema.Array(ToolSchema.String(), "Optional server list (DC hostnames/FQDNs). If omitted, domain controllers are discovered.")),
                ("domain_controller", ToolSchema.String("Optional domain controller override used for discovery and RootDSE reads.")),
                ("max_servers", ToolSchema.Integer("Maximum servers to test when discovering DCs (capped). Default 10.")),
                ("include_global_catalog", ToolSchema.Boolean("Also test Global Catalog ports 3268/3269. Default true.")),
                ("verify_certificate", ToolSchema.Boolean("When true, compute cert_chain_ok for LDAPS and prefer LDAPS only when the chain builds. Default true.")),
                ("identity", ToolSchema.String("Optional identity to validate via a quick LDAP search (sAMAccountName/UPN/CN/name).")),
                ("certificate_include_dns_names", ToolSchema.Array(ToolSchema.String(), "Optional DNS names that must appear in the LDAPS certificate SAN list.")),
                ("timeout_ms", ToolSchema.Integer("Per-endpoint timeout in milliseconds (capped). Default 5000.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLdapDiagnosticsTool"/> class.
    /// </summary>
    public AdLdapDiagnosticsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var timeoutMs = (int)Math.Min(Math.Max(arguments?.GetInt64("timeout_ms") ?? DefaultTimeoutMs, 200), MaxTimeoutMs);

        var includeGc = arguments?.GetBoolean("include_global_catalog", defaultValue: true) ?? true;
        var verifyCert = arguments?.GetBoolean("verify_certificate", defaultValue: true) ?? true;

        var identity = (arguments?.GetString("identity") ?? string.Empty).Trim();
        if (identity.Length == 0) {
            identity = string.Empty;
        }

        var includeDnsNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("certificate_include_dns_names"));

        var dc = ToolArgs.GetOptionalTrimmed(arguments, "domain_controller");
        if (string.IsNullOrWhiteSpace(dc)) {
            dc = Options.DomainController;
        }

        var requestedMaxServers = arguments?.GetInt64("max_servers");
        var maxServers = requestedMaxServers.HasValue && requestedMaxServers.Value > 0
            ? (int)Math.Min(requestedMaxServers.Value, MaxServersCap)
            : DefaultMaxServers;

        var servers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("servers"));

        var report = await LdapDiagnosticsReportBuilder.BuildAsync(
                new LdapDiagnosticsReportBuilder.Options {
                    DomainController = dc,
                    Servers = servers,
                    MaxServers = maxServers,
                    IncludeGlobalCatalog = includeGc,
                    VerifyCertificate = verifyCert,
                    Identity = identity,
                    CertificateIncludeDnsNames = includeDnsNames,
                    TimeoutMs = timeoutMs
                },
                cancellationToken)
            .ConfigureAwait(false);

        return BuildAutoTableResponse(
            arguments: arguments,
            model: report,
            sourceRows: report.Servers,
            viewRowsPath: "servers_view",
            title: "Active Directory: LDAP Diagnostics (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            scanned: report.Servers.Count);
    }
}
