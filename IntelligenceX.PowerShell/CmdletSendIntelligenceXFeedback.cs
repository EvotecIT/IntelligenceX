using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Uploads textual feedback to the app-server feedback endpoint.</para>
/// <para type="description">Use this cmdlet to send reproduction notes, quality feedback, or analyzer observations
/// collected during reviews and local runs.</para>
/// <example>
///  <para>Send feedback</para>
///  <code>Send-IntelligenceXFeedback -Content "The review missed the nullable warning."</code>
/// </example>
/// <example>
///  <para>Send feedback from a multi-line here-string</para>
///  <code>$note = "Reviewer suggestion: add timeout handling.`nObserved on Windows PS 7.5."; Send-IntelligenceXFeedback -Content $note</code>
/// </example>
/// <example>
///  <para>Send feedback with an explicit client</para>
///  <code>Send-IntelligenceXFeedback -Client $client -Content "MCP OAuth instructions were unclear for first-time users."</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommunications.Send, "IntelligenceXFeedback")]
public sealed class CmdletSendIntelligenceXFeedback : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Feedback text content to upload.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Content { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.UploadFeedbackAsync(Content, CancelToken).ConfigureAwait(false);
    }
}
