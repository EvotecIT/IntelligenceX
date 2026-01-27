using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using System.Diagnostics;

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
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        if (Raw.IsPresent) {
            var rawClient = ResolveAppServerClient(Client);
            var parameters = new JsonObject().Add("type", "chatgpt");
            var login = await rawClient.CallAsync("account/login/start", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(login);
            return;
        }

        var result = await resolved.LoginChatGptAsync(url => {
            WriteVerbose($"Open: {url}");
            TryOpenUrl(url);
        }, null, true, null, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }

    private static void TryOpenUrl(string url) {
        try {
            var psi = new ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        } catch {
            // Ignore failures to open browser.
        }
    }
}
