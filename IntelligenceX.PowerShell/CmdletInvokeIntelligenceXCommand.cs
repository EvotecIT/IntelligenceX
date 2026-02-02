using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Executes a command through the app-server.</para>
/// <para type="description">Runs a process via the app-server with optional sandbox settings and timeout.</para>
/// <example>
///  <para>Run a command in the repository root</para>
///  <code>Invoke-IntelligenceXCommand -Command @("git","status") -WorkingDirectory "C:\repo"</code>
/// </example>
/// <example>
///  <para>Run a command in a sandboxed workspace</para>
///  <code>Invoke-IntelligenceXCommand -Command @("dotnet","test") -SandboxType "workspace" -WorkingDirectory "C:\repo"</code>
/// </example>
/// <example>
///  <para>Run a command with a timeout</para>
///  <code>Invoke-IntelligenceXCommand -Command @("git","status") -TimeoutMs 5000</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXCommand")]
[OutputType(typeof(CommandExecResult), typeof(JsonValue))]
public sealed class CmdletInvokeIntelligenceXCommand : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Command and arguments.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string[] Command { get; set; } = [];

    /// <summary>
    /// <para type="description">Working directory for the command.</para>
    /// </summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// <para type="description">Sandbox type for execution.</para>
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
    /// <para type="description">Command timeout in milliseconds.</para>
    /// </summary>
    [Parameter]
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        var request = new CommandExecRequest(Command) {
            WorkingDirectory = WorkingDirectory,
            TimeoutMs = TimeoutMs
        };

        if (!string.IsNullOrWhiteSpace(SandboxType)) {
            request.SandboxPolicy = new SandboxPolicy(SandboxType!, NetworkAccess.IsPresent, WritableRoot);
        }

        if (Raw.IsPresent) {
            var commandArray = new JsonArray();
            foreach (var item in request.Command) {
                commandArray.Add(item);
            }
            var parameters = new JsonObject()
                .Add("command", commandArray);
            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) {
                parameters.Add("cwd", request.WorkingDirectory);
            }
            if (request.SandboxPolicy is not null) {
                parameters.Add("sandboxPolicy", SandboxPolicyJson.ToJson(request.SandboxPolicy));
            }
            if (request.TimeoutMs.HasValue) {
                parameters.Add("timeoutMs", request.TimeoutMs.Value);
            }
            var result = await resolved.CallAsync("command/exec", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ExecuteCommandAsync(request, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
