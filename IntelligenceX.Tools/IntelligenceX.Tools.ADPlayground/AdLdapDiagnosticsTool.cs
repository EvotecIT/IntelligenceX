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
    private sealed record LdapDiagnosticsRequest(
        int TimeoutMs,
        bool IncludeGlobalCatalog,
        bool VerifyCertificate,
        string Identity,
        IReadOnlyList<string> CertificateIncludeDnsNames,
        string? DomainController,
        int MaxServers,
        IReadOnlyList<string> Servers);

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
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<LdapDiagnosticsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var requestedMaxServers = reader.OptionalInt64("max_servers");
            var maxServers = requestedMaxServers.HasValue && requestedMaxServers.Value > 0
                ? (int)Math.Min(requestedMaxServers.Value, MaxServersCap)
                : DefaultMaxServers;

            return ToolRequestBindingResult<LdapDiagnosticsRequest>.Success(new LdapDiagnosticsRequest(
                TimeoutMs: reader.CappedInt32("timeout_ms", DefaultTimeoutMs, 200, MaxTimeoutMs),
                IncludeGlobalCatalog: reader.Boolean("include_global_catalog", defaultValue: true),
                VerifyCertificate: reader.Boolean("verify_certificate", defaultValue: true),
                Identity: reader.OptionalString("identity") ?? string.Empty,
                CertificateIncludeDnsNames: reader.DistinctStringArray("certificate_include_dns_names"),
                DomainController: reader.OptionalString("domain_controller"),
                MaxServers: maxServers,
                Servers: reader.DistinctStringArray("servers")));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<LdapDiagnosticsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var dc = request.DomainController;
        if (string.IsNullOrWhiteSpace(dc)) {
            dc = Options.DomainController;
        }

        var report = await LdapDiagnosticsReportBuilder.BuildAsync(
                new LdapDiagnosticsReportBuilder.Options {
                    DomainController = dc,
                    Servers = request.Servers,
                    MaxServers = request.MaxServers,
                    IncludeGlobalCatalog = request.IncludeGlobalCatalog,
                    VerifyCertificate = request.VerifyCertificate,
                    Identity = request.Identity,
                    CertificateIncludeDnsNames = request.CertificateIncludeDnsNames,
                    TimeoutMs = request.TimeoutMs
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: report,
            sourceRows: report.Servers,
            viewRowsPath: "servers_view",
            title: "Active Directory: LDAP Diagnostics (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            scanned: report.Servers.Count);
    }
}
