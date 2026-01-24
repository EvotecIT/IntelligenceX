using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Stops the Codex app-server client and releases resources.</para>
/// </summary>
[Cmdlet(VerbsCommunications.Disconnect, "IntelligenceX")]
public sealed class CmdletDisconnectIntelligenceX : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to disconnect. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override Task ProcessRecordAsync() {
        if (Client is null && ClientContext.DefaultClient is null) {
            return Task.CompletedTask;
        }

        var resolved = ResolveClient(Client);
        resolved.Dispose();
        ClearDefaultClient(resolved);
        ClientContext.DefaultThreadId = null;
        ClientContext.Initialized = false;
        return Task.CompletedTask;
    }
}
