using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Resumes an existing thread.</para>
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
    /// <para type="description">Thread identifier.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

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
