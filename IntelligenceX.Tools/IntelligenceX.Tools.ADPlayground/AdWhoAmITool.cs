using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns runtime identity + basic Active Directory context info (read-only).
/// </summary>
public sealed class AdWhoAmITool : ActiveDirectoryToolBase, ITool {
    private sealed record WhoAmIRequest;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_whoami",
        "Return the current process identity used for Active Directory operations + basic domain context (read-only).",
        ToolSchema.Object()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdWhoAmITool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdWhoAmITool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON string result.</returns>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<WhoAmIRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<WhoAmIRequest>.Success(new WhoAmIRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<WhoAmIRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var result = AdWhoAmIService.Query(
            options: new AdWhoAmIService.WhoAmIQueryOptions {
                DomainController = Options.DomainController,
                DefaultSearchBaseDn = Options.DefaultSearchBaseDn
            },
            cancellationToken: cancellationToken);

        var facts = new[] {
            ("User", result.User),
            ("Domain controller", result.DomainController),
            ("Default naming context", result.DefaultNamingContext),
            ("RootDSE dnsHostName", result.RootDseDnsHostName)
        };
        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: result,
            title: "Active Directory: WhoAmI",
            facts: facts,
            meta: ToolOutputHints.Meta(count: facts.Length, truncated: false),
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}
