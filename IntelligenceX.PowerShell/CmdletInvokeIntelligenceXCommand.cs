using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Executes a command through the app-server.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXCommand")]
[OutputType(typeof(CommandExecResult))]
public sealed class CmdletInvokeIntelligenceXCommand : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Command and arguments.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string[] Command { get; set; } = [];

    /// <summary>
    /// <para type="description">Working directory for the command.</para>
    /// </summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// <para type="description">Sandbox type for execution.</para>
    /// </summary>
    [Parameter]
    public string? SandboxType { get; set; }

    /// <summary>
    /// <para type="description">Enable network access for sandboxed runs.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter NetworkAccess { get; set; }

    /// <summary>
    /// <para type="description">Writable root paths for sandboxed runs.</para>
    /// </summary>
    [Parameter]
    public string[]? WritableRoot { get; set; }

    /// <summary>
    /// <para type="description">Command timeout in milliseconds.</para>
    /// </summary>
    [Parameter]
    public int? TimeoutMs { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var request = new CommandExecRequest(Command) {
            WorkingDirectory = WorkingDirectory,
            TimeoutMs = TimeoutMs
        };

        if (!string.IsNullOrWhiteSpace(SandboxType)) {
            request.SandboxPolicy = new SandboxPolicy(SandboxType!, NetworkAccess.IsPresent, WritableRoot);
        }

        var result = await resolved.ExecuteCommandAsync(request, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
