using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Sends a message to a thread and starts a turn.</para>
/// <para type="description">Queues a user message on an existing thread. Returns the created turn so you can
/// poll for output with Get-IntelligenceXTurnOutput.</para>
/// <example>
///  <para>Send a message in an existing thread</para>
///  <code>Send-IntelligenceXMessage -ThreadId $thread.id -Text "Review this diff."</code>
/// </example>
/// <example>
///  <para>Send a message with a temporary model override</para>
///  <code>Send-IntelligenceXMessage -ThreadId $thread.id -Text "Summarize changes." -Model "gpt-5.3-codex"</code>
/// </example>
/// <example>
///  <para>Send a message with a workspace sandbox</para>
///  <code>Send-IntelligenceXMessage -ThreadId $thread.id -Text "Run tests" -SandboxType "workspace" -NetworkAccess -WritableRoot "C:\repo"</code>
/// </example>
/// <example>
///  <para>Return raw JSON output</para>
///  <code>Send-IntelligenceXMessage -ThreadId $thread.id -Text "Status?" -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommunications.Send, "IntelligenceXMessage")]
[OutputType(typeof(TurnInfo), typeof(JsonValue))]
public sealed class CmdletSendIntelligenceXMessage : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Thread identifier to send the message to.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Message text to send.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Optional model override.</para>
    /// </summary>
    [Parameter]
    public string? Model { get; set; }

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
    /// <para type="description">Sandbox type (for example 'workspace' or 'danger-full-access').</para>
    /// </summary>
    [Parameter]
    public string? SandboxType { get; set; }

    /// <summary>
    /// <para type="description">Enable network access for sandboxed runs.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter NetworkAccess { get; set; }

    /// <summary>
    /// <para type="description">Writable root paths for sandboxed runs.</para>
    /// </summary>
    [Parameter]
    public string[]? WritableRoot { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        SandboxPolicy? sandbox = null;
        if (!string.IsNullOrWhiteSpace(SandboxType)) {
            sandbox = new SandboxPolicy(SandboxType!, NetworkAccess.IsPresent, WritableRoot);
        }

        if (Raw.IsPresent) {
            var input = new JsonArray()
                .Add(new JsonObject()
                    .Add("type", "text")
                    .Add("text", Text));
            var parameters = new JsonObject()
                .Add("threadId", ThreadId)
                .Add("input", input);
            if (!string.IsNullOrWhiteSpace(Model)) {
                parameters.Add("model", Model);
            }
            if (!string.IsNullOrWhiteSpace(CurrentDirectory)) {
                parameters.Add("cwd", CurrentDirectory);
            }
            if (!string.IsNullOrWhiteSpace(ApprovalPolicy)) {
                parameters.Add("approvalPolicy", ApprovalPolicy);
            }
            if (sandbox is not null) {
                parameters.Add("sandboxPolicy", SandboxPolicyJson.ToJson(sandbox));
            }
            var turn = await resolved.CallAsync("turn/start", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(turn);
        } else {
            var turn = await resolved.StartTurnAsync(ThreadId, Text, Model, CurrentDirectory, ApprovalPolicy, sandbox, CancelToken)
                .ConfigureAwait(false);
            WriteObject(turn);
        }
    }
}
