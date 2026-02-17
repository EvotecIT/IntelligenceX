using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Requests user input through the app-server.</para>
/// <para type="description">Prompts for one to three questions and returns collected answers in order.
/// Useful for interactive script checkpoints that need explicit user confirmation or values.</para>
/// <example>
///  <para>Ask for two inputs</para>
///  <code>Request-IntelligenceXUserInput -Questions "Repo name?", "Branch?"</code>
/// </example>
/// <example>
///  <para>Return raw JSON response</para>
///  <code>Request-IntelligenceXUserInput -Questions "Continue?" -Raw</code>
/// </example>
/// <example>
///  <para>Use responses in script flow</para>
///  <code>$response = Request-IntelligenceXUserInput -Questions "PR number?", "Merge now?"; $response.Answers</code>
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
    /// <para type="description">Questions to ask (minimum 1, maximum 3).</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string[] Questions { get; set; } = [];

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed response data.</para>
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
