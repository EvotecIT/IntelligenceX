using System.Threading;
using System.Threading.Tasks;
using ComputerX.PowerShellRuntime;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Lists available PowerShell runtime hosts on the local machine.
/// </summary>
public sealed class PowerShellHostsTool : PowerShellToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "powershell_hosts",
        "List available local PowerShell hosts (pwsh and/or windows_powershell).",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellHostsTool"/> class.
    /// </summary>
    public PowerShellHostsTool(PowerShellToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var hosts = PowerShellCommandQueryExecutor.GetAvailableHosts();
        var model = new {
            enabled = Options.Enabled,
            available_hosts = hosts,
            preferred_default = hosts.Count > 0 ? hosts[0] : "none"
        };

        var summary = ToolMarkdown.SummaryFacts(
            title: "PowerShell Hosts",
            facts: new[] {
                ("Enabled", Options.Enabled ? "true" : "false"),
                ("Available", hosts.Count.ToString())
            });

        return Task.FromResult(ToolResponse.OkModel(
            model: model,
            meta: ToolOutputHints.Meta(count: hosts.Count, truncated: false),
            summaryMarkdown: summary));
    }
}
