using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Requests user input through the app-server.</para>
/// <para type="description">Prompts for one to three questions and returns the responses.</para>
/// <example>
///  <para>Ask for two inputs</para>
///  <code>Request-IntelligenceXUserInput -Questions "Repo name?", "Branch?"</code>
/// </example>
/// <example>
///  <para>Return raw JSON response</para>
///  <code>Request-IntelligenceXUserInput -Questions "Continue?" -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Request, "IntelligenceXUserInput")]
[OutputType(typeof(UserInputResponse), typeof(JsonValue))]
public sealed class CmdletRequestIntelligenceXUserInput : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Questions to ask (1-3).</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string[] Questions { get; set; } = [];

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var array = new JsonArray();
            foreach (var question in Questions) {
                array.Add(question);
            }
            var parameters = new JsonObject().Add("questions", array);
            var result = await resolved.CallAsync("tool/requestUserInput", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.RequestUserInputAsync(Questions, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
