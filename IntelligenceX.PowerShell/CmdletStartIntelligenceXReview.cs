using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Starts a review flow for a thread.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "IntelligenceXReview")]
[OutputType(typeof(ReviewStartResult), typeof(JsonValue))]
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

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    protected override async Task ProcessRecordAsync() {
        var resolved = ResolveClient(Client);
        var target = TargetType switch {
            "uncommittedChanges" => ReviewTarget.UncommittedChanges(),
            "baseBranch" => ReviewTarget.BaseBranch(TargetValue ?? string.Empty),
            "commit" => ReviewTarget.Commit(TargetValue ?? string.Empty),
            "custom" => ReviewTarget.Custom(TargetValue ?? string.Empty),
            _ => ReviewTarget.Custom(TargetValue ?? string.Empty)
        };

        if (Raw.IsPresent) {
            var targetPayload = TargetType switch {
                "uncommittedChanges" => new JsonObject().Add("type", "uncommittedChanges"),
                "baseBranch" => new JsonObject().Add("type", "baseBranch").Add("branch", TargetValue ?? string.Empty),
                "commit" => new JsonObject().Add("type", "commit").Add("sha", TargetValue ?? string.Empty),
                "custom" => new JsonObject().Add("type", "custom").Add("text", TargetValue ?? string.Empty),
                _ => new JsonObject().Add("type", "custom").Add("text", TargetValue ?? string.Empty)
            };

            var parameters = new JsonObject()
                .Add("threadId", ThreadId)
                .Add("delivery", Delivery)
                .Add("target", targetPayload);
            var result = await resolved.CallAsync("review/start", parameters, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        } else {
            var result = await resolved.StartReviewAsync(ThreadId, Delivery, target, CancelToken).ConfigureAwait(false);
            WriteObject(result);
        }
    }
}
