using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Lists collaboration modes available in the current app-server runtime.</para>
/// <para type="description">Use this cmdlet to discover valid collaboration mode values before setting them in
/// configuration or passing them to review/chat workflows.</para>
/// <example>
///  <para>List collaboration modes</para>
///  <code>Get-IntelligenceXCollaborationMode</code>
/// </example>
/// <example>
///  <para>Show mode names and mapped mode values</para>
///  <code>(Get-IntelligenceXCollaborationMode).Modes | Select-Object Name, Mode, Model</code>
/// </example>
/// <example>
///  <para>Return raw JSON response</para>
///  <code>Get-IntelligenceXCollaborationMode -Raw</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXCollaborationMode")]
[OutputType(typeof(CollaborationModeListResult), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXCollaborationMode : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed mode models.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        if (Raw.IsPresent) {
            var result = await resolved.CallAsync("collaborationMode/list", (JsonObject?)null, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.ListCollaborationModesAsync(CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
