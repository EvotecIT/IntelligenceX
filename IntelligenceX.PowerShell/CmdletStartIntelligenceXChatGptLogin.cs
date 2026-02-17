using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using System.Diagnostics;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts the ChatGPT login flow and returns the authorization URL.</para>
/// <para type="description">Opens the browser to complete the ChatGPT OAuth flow. Pair with Wait-IntelligenceXLogin
/// to poll for completion and store the resulting credentials.</para>
/// <example>
///  <para>Start login and open the browser</para>
///  <code>Start-IntelligenceXChatGptLogin</code>
/// </example>
/// <example>
///  <para>Return the raw JSON response for custom handling</para>
///  <code>Start-IntelligenceXChatGptLogin -Raw</code>
/// </example>
/// <example>
///  <para>Start login and explicitly wait for completion</para>
///  <code>$login = Start-IntelligenceXChatGptLogin; Wait-IntelligenceXLogin -LoginId $login.LoginId</code>
/// </example>
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
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed login data.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
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
