using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Authenticates using an OpenAI API key.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXApiKeyLogin")]
public sealed class CmdletStartIntelligenceXApiKeyLogin : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">API key to authenticate with.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ApiKey { get; set; } = string.Empty;

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        await resolved.LoginWithApiKeyAsync(ApiKey, CancelToken).ConfigureAwait(false);
    }
}
