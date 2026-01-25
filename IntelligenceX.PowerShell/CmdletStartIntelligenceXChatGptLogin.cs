using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts the ChatGPT login flow and returns the authorization URL.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXChatGptLogin")]
[OutputType(typeof(ChatGptLoginStart), typeof(JsonValue))]
public sealed class CmdletStartIntelligenceXChatGptLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject().Add("type", "chatgpt");
            var login = await resolved.CallAsync("account/login/start", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(login);
        } else {
            var login = await resolved.StartChatGptLoginAsync(CancelToken).ConfigureAwait(false);
            WriteObject(login);
        }
    }
}
