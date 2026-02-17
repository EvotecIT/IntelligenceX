using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Creates a new thread fork from an existing thread's history.</para>
/// <para type="description">Use this when you want to branch a conversation without mutating the original thread.
/// The fork inherits prior context and gets a new thread id.</para>
/// <example>
///  <para>Fork a thread</para>
///  <code>New-IntelligenceXThreadFork -ThreadId $thread.Id</code>
/// </example>
/// <example>
///  <para>Fork and continue conversation in the new thread</para>
///  <code>$fork = New-IntelligenceXThreadFork -ThreadId $thread.Id; Send-IntelligenceXMessage -ThreadId $fork.Id -Text "Take a different approach."</code>
/// </example>
/// <example>
///  <para>Return raw JSON response</para>
///  <code>New-IntelligenceXThreadFork -ThreadId $thread.Id -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.New, "IntelligenceXThreadFork")]
[OutputType(typeof(ThreadInfo), typeof(JsonValue))]
public sealed class CmdletForkIntelligenceXThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Identifier of the source thread to fork.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed thread info.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject().Add("threadId", ThreadId);
            var thread = await resolved.CallAsync("thread/fork", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        } else {
            var thread = await resolved.ForkThreadAsync(ThreadId, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        }
    }
}
