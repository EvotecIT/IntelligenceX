using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Stops the Codex app-server client and releases resources.</para>
/// <para type="description">Clears the default client and any diagnostic subscriptions. If no client is active, no action is taken.</para>
/// <example>
///  <para>Disconnect the active client</para>
///  <code>Disconnect-IntelligenceX</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommunications.Disconnect, "IntelligenceX")]
public sealed class CmdletDisconnectIntelligenceX : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to disconnect. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <inheritdoc/>
    protected override Task ProcessRecordAsync() {
        if (Client is null && ClientContext.DefaultClient is null) {
            return Task.CompletedTask;
        }

        var resolved = ResolveClient(Client);
        resolved.Dispose();
        ClearDefaultClient(resolved);
        ClientContext.Diagnostics?.Dispose();
        ClientContext.Diagnostics = null;
        ClientContext.DefaultThreadId = null;
        ClientContext.Initialized = false;
        return Task.CompletedTask;
    }
}
