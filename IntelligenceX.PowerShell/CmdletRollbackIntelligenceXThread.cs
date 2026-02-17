using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Rolls back the last N turns from a thread.</para>
/// <para type="description">Removes recent turns from a thread so you can re-run, correct, or branch from an earlier
/// conversation state.</para>
/// <example>
///  <para>Rollback the last two turns</para>
///  <code>Restore-IntelligenceXThread -ThreadId $thread.Id -Turns 2</code>
/// </example>
/// <example>
///  <para>Rollback one turn and resend an adjusted message</para>
///  <code>Restore-IntelligenceXThread -ThreadId $thread.Id -Turns 1; Send-IntelligenceXMessage -ThreadId $thread.Id -Text "Use a shorter summary."</code>
/// </example>
/// <example>
///  <para>Return raw JSON response</para>
///  <code>Restore-IntelligenceXThread -ThreadId $thread.Id -Turns 1 -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsData.Restore, "IntelligenceXThread")]
[OutputType(typeof(ThreadInfo), typeof(JsonValue))]
public sealed class CmdletRollbackIntelligenceXThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Identifier of the thread to modify.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Number of most recent turns to remove.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public int Turns { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed thread info.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject()
                .Add("threadId", ThreadId)
                .Add("turns", Turns);
            var thread = await resolved.CallAsync("thread/rollback", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        } else {
            var thread = await resolved.RollbackThreadAsync(ThreadId, Turns, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        }
    }
}
