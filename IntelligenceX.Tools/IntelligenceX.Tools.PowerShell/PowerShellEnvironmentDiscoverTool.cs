using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Returns runtime and policy discovery details for IX.PowerShell planning.
/// </summary>
public sealed class PowerShellEnvironmentDiscoverTool : PowerShellToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "powershell_environment_discover",
        "Discover IX.PowerShell runtime hosts (pwsh/windows_powershell/cmd) and execution policy (enabled/read-write options). Call this before powershell_run.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellEnvironmentDiscoverTool"/> class.
    /// </summary>
    public PowerShellEnvironmentDiscoverTool(PowerShellToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var hosts = GetAvailableRuntimeHosts();
        var preferred = hosts.Count > 0 ? hosts[0] : "none";

        var model = new {
            enabled = Options.Enabled,
            policy = new {
                allow_write = Options.AllowWrite,
                require_explicit_write_flag = Options.RequireExplicitWriteFlag,
                enable_mutation_heuristic = Options.EnableMutationHeuristic
            },
            runtime = new {
                available_hosts = hosts,
                preferred_default = preferred,
                has_any_host = hosts.Count > 0,
                cmd_available = IsCmdHostAvailable()
            },
            limits = new {
                default_timeout_ms = Options.DefaultTimeoutMs,
                max_timeout_ms = Options.MaxTimeoutMs,
                default_max_output_chars = Options.DefaultMaxOutputChars,
                max_output_chars = Options.MaxOutputChars
            }
        };

        var summary = ToolMarkdown.SummaryFacts(
            title: "PowerShell Environment",
            facts: new[] {
                ("Enabled", Options.Enabled ? "true" : "false"),
                ("AllowWrite", Options.AllowWrite ? "true" : "false"),
                ("RequireExplicitWrite", Options.RequireExplicitWriteFlag ? "true" : "false"),
                ("AvailableHosts", hosts.Count.ToString())
            });

        return Task.FromResult(ToolResponse.OkModel(
            model: model,
            meta: ToolOutputHints.Meta(count: hosts.Count, truncated: false),
            summaryMarkdown: summary));
    }
}
