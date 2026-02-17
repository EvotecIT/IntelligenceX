using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Disconnects the active IntelligenceX client and clears local session context.</para>
/// <para type="description">Releases app-server/native client resources, clears diagnostics subscriptions, and resets
/// default thread/initialization state. If no client is active, the cmdlet exits without error.</para>
/// <example>
///  <para>Disconnect the active client</para>
///  <code>Disconnect-IntelligenceX</code>
/// </example>
/// <example>
///  <para>Disconnect a specific client object</para>
///  <code>Disconnect-IntelligenceX -Client $client</code>
/// </example>
/// <example>
///  <para>Clean up at the end of a script</para>
///  <code>try { Invoke-IntelligenceXChat -Text "Summarize changes" } finally { Disconnect-IntelligenceX }</code>
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
