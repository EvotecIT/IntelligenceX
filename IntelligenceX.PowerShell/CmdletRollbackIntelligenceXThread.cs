using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Rolls back the last N turns from a thread.</para>
/// <para type="description">Removes recent turns from a thread so you can re-run or correct the conversation.</para>
/// <example>
///  <para>Rollback the last two turns</para>
///  <code>Restore-IntelligenceXThread -ThreadId $thread.id -Turns 2</code>
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
    /// <para type="description">Thread identifier.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Number of turns to roll back.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public int Turns { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
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
