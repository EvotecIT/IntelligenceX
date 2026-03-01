using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Reads high-level Active Directory RootDSE information.
/// </summary>
public sealed class AdDomainInfoTool : ActiveDirectoryToolBase, ITool {
    private sealed record DomainInfoRequest(string? DomainController);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_info",
        "Read basic Active Directory domain/forest information from RootDSE (read-only).",
        ToolSchema.Object(
                ("domain_controller", ToolSchema.String("Optional domain controller override (host/FQDN).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainInfoTool"/> class.
    /// </summary>
    public AdDomainInfoTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<DomainInfoRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader =>
            ToolRequestBindingResult<DomainInfoRequest>.Success(new DomainInfoRequest(
                DomainController: reader.OptionalString("domain_controller"))));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DomainInfoRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainController = context.Request.DomainController ?? Options.DomainController;
        var result = DomainInfoService.Query(domainController, cancellationToken);

        var model = new {
            result.DomainController,
            result.DnsDomainName,
            result.ForestDnsName,
            RootDse = new LdapToolOutputRow {
                Attributes = result.RootDse.Attributes,
                TruncatedAttributes = result.RootDse.TruncatedAttributes
            }
        };

        var facts = new[] {
            ("Domain controller", result.DomainController),
            ("DNS domain name", result.DnsDomainName),
            ("Forest DNS name", result.ForestDnsName)
        };

        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: model,
            title: "Active Directory: Domain Info",
            facts: facts,
            meta: ToolOutputHints.Meta(count: facts.Length, truncated: false),
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}
