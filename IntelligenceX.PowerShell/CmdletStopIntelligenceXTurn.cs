using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Interrupts a running turn for a thread.</para>
/// <para type="description">Requests cancellation for an in-progress turn. Use this for long-running responses,
/// accidental prompts, or when you need to restart execution with different instructions.</para>
/// <example>
///  <para>Stop a running turn</para>
///  <code>Stop-IntelligenceXTurn -ThreadId $thread.Id -TurnId $turn.Id</code>
/// </example>
/// <example>
///  <para>Stop the turn returned by Send-IntelligenceXMessage</para>
///  <code>$turn = Send-IntelligenceXMessage -ThreadId $thread.Id -Text "Generate a long report"; Stop-IntelligenceXTurn -ThreadId $thread.Id -TurnId $turn.Id</code>
/// </example>
/// <example>
///  <para>Interrupt with an explicit client</para>
///  <code>Stop-IntelligenceXTurn -Client $client -ThreadId $thread.Id -TurnId $turn.Id</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Stop, "IntelligenceXTurn")]
public sealed class CmdletStopIntelligenceXTurn : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Identifier of the thread that owns the running turn.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Identifier of the turn to interrupt.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string TurnId { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.InterruptTurnAsync(ThreadId, TurnId, CancelToken).ConfigureAwait(false);
    }
}
