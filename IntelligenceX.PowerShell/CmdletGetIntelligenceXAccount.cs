using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Returns the current account details.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXAccount")]
[OutputType(typeof(AccountInfo))]
public sealed class CmdletGetIntelligenceXAccount : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var account = await resolved.ReadAccountAsync(CancelToken).ConfigureAwait(false);
        WriteObject(account);
    }
}
