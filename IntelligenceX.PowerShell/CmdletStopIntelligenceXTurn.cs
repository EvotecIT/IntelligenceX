using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Interrupts a running turn.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Stop, "IntelligenceXTurn")]
public sealed class CmdletStopIntelligenceXTurn : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Thread identifier.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Turn identifier.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string TurnId { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.InterruptTurnAsync(ThreadId, TurnId, CancelToken).ConfigureAwait(false);
    }
}
