using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Lists available PowerShell runtime hosts on the local machine.
/// </summary>
public sealed class PowerShellHostsTool : PowerShellToolBase, ITool {
    private sealed record HostsRequest;

    private static readonly ToolDefinition DefinitionValue = new(
        "powershell_hosts",
        "List available local shell hosts (pwsh/windows_powershell/cmd).",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellHostsTool"/> class.
    /// </summary>
    public PowerShellHostsTool(PowerShellToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<HostsRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<HostsRequest>.Success(new HostsRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<HostsRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var hosts = GetAvailableRuntimeHosts();
        var model = new {
            enabled = Options.Enabled,
            available_hosts = hosts,
            preferred_default = hosts.Count > 0 ? hosts[0] : "none",
            cmd_available = IsCmdHostAvailable()
        };

        var summary = ToolMarkdown.SummaryFacts(
            title: "PowerShell Hosts",
            facts: new[] {
                ("Enabled", Options.Enabled ? "true" : "false"),
                ("Available", hosts.Count.ToString())
            });

        return Task.FromResult(ToolResultV2.OkModel(
            model: model,
            meta: ToolOutputHints.Meta(count: hosts.Count, truncated: false),
            summaryMarkdown: summary));
    }
}
