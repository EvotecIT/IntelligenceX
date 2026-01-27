using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Uploads feedback content.</para>
/// </summary>
[Cmdlet(VerbsCommunications.Send, "IntelligenceXFeedback")]
public sealed class CmdletSendIntelligenceXFeedback : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Feedback content.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Content { get; set; } = string.Empty;

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.UploadFeedbackAsync(Content, CancelToken).ConfigureAwait(false);
    }
}
