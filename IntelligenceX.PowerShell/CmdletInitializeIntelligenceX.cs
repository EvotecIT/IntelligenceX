using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Initializes the client handshake with the app-server.</para>
/// <para type="description">Sends client identity metadata to the app-server. This is required for some app-server flows.</para>
/// <example>
///  <para>Initialize the app-server session</para>
///  <code>Initialize-IntelligenceX -Name "ix-cli" -Title "IntelligenceX CLI" -Version "0.1.0"</code>
/// </example>
/// </summary>
[Cmdlet("Initialize", "IntelligenceX")]
public sealed class CmdletInitializeIntelligenceX : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to initialize. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Client name sent to the app-server.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Client title sent to the app-server.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Client version sent to the app-server.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Version { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var info = new ClientInfo(Name, Title, Version);
        await resolved.InitializeAsync(info, CancelToken).ConfigureAwait(false);
    }
}
