using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Sends a message to a thread and starts a turn.</para>
/// </summary>
[Cmdlet(VerbsCommunications.Send, "IntelligenceXMessage")]
[OutputType(typeof(TurnInfo))]
public sealed class CmdletSendIntelligenceXMessage : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Thread identifier to send the message to.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Message text to send.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Optional model override.</para>
    /// </summary>
    [Parameter]
    public string? Model { get; set; }

    /// <summary>
    /// <para type="description">Working directory to pass to the app-server.</para>
    /// </summary>
    [Parameter]
    public string? CurrentDirectory { get; set; }

    /// <summary>
    /// <para type="description">Approval policy name to use.</para>
    /// </summary>
    [Parameter]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// <para type="description">Sandbox type (for example 'workspace' or 'danger-full-access').</para>
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

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        SandboxPolicy? sandbox = null;
        if (!string.IsNullOrWhiteSpace(SandboxType)) {
            sandbox = new SandboxPolicy(SandboxType!, NetworkAccess.IsPresent, WritableRoot);
        }

        var turn = await resolved.StartTurnAsync(ThreadId, Text, Model, CurrentDirectory, ApprovalPolicy, sandbox, CancelToken)
            .ConfigureAwait(false);
        WriteObject(turn);
    }
}
