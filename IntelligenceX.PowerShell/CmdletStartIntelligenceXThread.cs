using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Creates a new conversation thread.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXThread")]
[OutputType(typeof(ThreadInfo), typeof(JsonValue))]
public sealed class CmdletStartIntelligenceXThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Model identifier to use (for example gpt-5.1-codex).</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Working directory to pass to the app-server.</para>
    /// </summary>
    [Parameter]
    public string? CurrentDirectory { get; set; }

    /// <summary>
    /// <para type="description">Approval policy name to use.</para>
    /// </summary>
    [Parameter]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// <para type="description">Sandbox mode to use (for example 'workspace' or 'danger-full-access').</para>
    /// </summary>
    [Parameter]
    public string? Sandbox { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var parameters = new JsonObject().Add("model", Model);
            if (!string.IsNullOrWhiteSpace(CurrentDirectory)) {
                parameters.Add("cwd", CurrentDirectory);
            }
            if (!string.IsNullOrWhiteSpace(ApprovalPolicy)) {
                parameters.Add("approvalPolicy", ApprovalPolicy);
            }
            if (!string.IsNullOrWhiteSpace(Sandbox)) {
                parameters.Add("sandbox", Sandbox);
            }
            var thread = await resolved.CallAsync("thread/start", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        } else {
            var thread = await resolved.StartThreadAsync(Model, CurrentDirectory, ApprovalPolicy, Sandbox, CancelToken).ConfigureAwait(false);
            WriteObject(thread);
        }
    }
}
