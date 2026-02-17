using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Archives an existing thread so it no longer appears in active workflows.</para>
/// <para type="description">Use this cmdlet to hide completed or obsolete conversations from normal thread listings while
/// preserving history. The archive operation is non-destructive and can be used for housekeeping.</para>
/// <example>
///  <para>Archive a thread</para>
///  <code>Backup-IntelligenceXThread -ThreadId $thread.Id</code>
/// </example>
/// <example>
///  <para>Archive a thread after a completed review</para>
///  <code>$review = Start-IntelligenceXReview -ThreadId $thread.Id -Delivery immediate -TargetType uncommittedChanges; Backup-IntelligenceXThread -ThreadId $thread.Id</code>
/// </example>
/// <example>
///  <para>Archive using an explicit client instance</para>
///  <code>$client = Connect-IntelligenceX; Backup-IntelligenceXThread -Client $client -ThreadId $thread.Id</code>
/// </example>
/// </summary>
[Cmdlet(VerbsData.Backup, "IntelligenceXThread")]
public sealed class CmdletArchiveIntelligenceXThread : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public IntelligenceXClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Identifier of the thread to archive.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveAppServerClient(Client);
        await resolved.ArchiveThreadAsync(ThreadId, CancelToken).ConfigureAwait(false);
    }
}
