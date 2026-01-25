using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts a review flow for a thread.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXReview")]
[OutputType(typeof(ReviewStartResult))]
public sealed class CmdletStartIntelligenceXReview : IntelligenceXCmdlet {
    /// <summary>
    /// <para type="description">Client instance to use. Defaults to the active client.</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    public AppServerClient? Client { get; set; }

    /// <summary>
    /// <para type="description">Thread identifier.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Delivery mode (for example immediate).</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string Delivery { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Target type: uncommittedChanges, baseBranch, commit, custom.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Target value (branch name, commit SHA, or custom text).</para>
    /// </summary>
    [Parameter]
    public string? TargetValue { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var target = TargetType switch {
            "uncommittedChanges" => ReviewTarget.UncommittedChanges(),
            "baseBranch" => ReviewTarget.BaseBranch(TargetValue ?? string.Empty),
            "commit" => ReviewTarget.Commit(TargetValue ?? string.Empty),
            "custom" => ReviewTarget.Custom(TargetValue ?? string.Empty),
            _ => ReviewTarget.Custom(TargetValue ?? string.Empty)
        };

        var result = await resolved.StartReviewAsync(ThreadId, Delivery, target, CancelToken).ConfigureAwait(false);
        WriteObject(result);
    }
}
