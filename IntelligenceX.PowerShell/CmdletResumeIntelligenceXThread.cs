using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Resumes an existing thread so new messages can be sent to it.</para>
/// <para type="description">Loads thread context back into the active app-server session and returns thread metadata.
/// Useful after reconnecting or when switching between multiple threads.</para>
/// <example>
///  <para>Resume a thread by id</para>
///  <code>Resume-IntelligenceXThread -ThreadId $thread.Id</code>
/// </example>
/// <example>
///  <para>Resume and send a follow-up message</para>
///  <code>$active = Resume-IntelligenceXThread -ThreadId $thread.Id; Send-IntelligenceXMessage -ThreadId $active.Id -Text "Continue from previous context."</code>
/// </example>
/// <example>
///  <para>Return raw JSON response</para>
///  <code>Resume-IntelligenceXThread -ThreadId $thread.Id -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Resume, "IntelligenceXThread")]
[OutputType(typeof(ThreadInfo), typeof(JsonValue))]
public sealed class CmdletResumeIntelligenceXThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Identifier of the thread to resume.</para>
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
            var thread = await resolved.CallAsync("thread/resume", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        } else {
            var thread = await resolved.ResumeThreadAsync(ThreadId, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        }
    }
}
