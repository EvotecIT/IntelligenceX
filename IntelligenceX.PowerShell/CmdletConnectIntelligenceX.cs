using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts the Codex app-server and returns a client instance.</para>
/// </summary>
[Cmdlet(VerbsCommunications.Connect, "IntelligenceX")]
[OutputType(typeof(AppServerClient))]
public sealed class CmdletConnectIntelligenceX : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Path to the codex executable. Defaults to 'codex' on PATH.</para>
    /// </summary>
    [Parameter]
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// <para type="description">Arguments to pass to the app-server. Defaults to 'app-server'.</para>
    /// </summary>
    [Parameter]
    public string? Arguments { get; set; }

    /// <summary>
    /// <para type="description">Working directory for the app-server process.</para>
    /// </summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    protected override async Task ProcessRecordAsync() {
        var options = new AppServerOptions();
        if (!string.IsNullOrWhiteSpace(ExecutablePath)) {
            options.ExecutablePath = ExecutablePath!;
        }
        if (!string.IsNullOrWhiteSpace(Arguments)) {
            options.Arguments = Arguments!;
        }
        if (!string.IsNullOrWhiteSpace(WorkingDirectory)) {
            options.WorkingDirectory = WorkingDirectory!;
        }

        var client = await AppServerClient.StartAsync(options, CancelToken).ConfigureAwait(false);
        SetDefaultClient(client);
        WriteObject(client);
    }
}
