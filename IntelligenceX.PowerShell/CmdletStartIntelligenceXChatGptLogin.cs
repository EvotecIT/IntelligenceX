using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts the ChatGPT login flow and returns the authorization URL.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXChatGptLogin")]
[OutputType(typeof(ChatGptLoginStart))]
public sealed class CmdletStartIntelligenceXChatGptLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var login = await resolved.StartChatGptLoginAsync(CancelToken).ConfigureAwait(false);
        WriteObject(login);
    }
}
