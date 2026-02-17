using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Returns the current account details.</para>
/// <para type="description">Returns the account identity for the active session, such as email and account id.
/// Useful to confirm which credential bundle is currently active.</para>
/// <example>
///  <para>Get account info</para>
///  <code>Get-IntelligenceXAccount</code>
/// </example>
/// <example>
///  <para>Get raw JSON account payload</para>
///  <code>Get-IntelligenceXAccount -Raw</code>
/// </example>
/// <example>
///  <para>Verify account after login in a script</para>
///  <code>$login = Start-IntelligenceXChatGptLogin; Wait-IntelligenceXLogin -LoginId $login.LoginId; Get-IntelligenceXAccount</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "IntelligenceXAccount")]
[OutputType(typeof(AccountInfo), typeof(JsonValue))]
public sealed class CmdletGetIntelligenceXAccount : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Returns the raw JSON-RPC payload instead of typed account info.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        if (Raw.IsPresent) {
            var rawClient = ResolveAppServerClient(Client);
            var account = await rawClient.CallAsync("account/read", (JsonObject?)null, CancelToken).ConfigureAwait(false);
            WriteObject(account);
            return;
        }
        var info = await resolved.GetAccountAsync(CancelToken).ConfigureAwait(false);
        WriteObject(info);
    }
}
