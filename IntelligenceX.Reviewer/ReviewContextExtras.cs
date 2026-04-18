using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewContextExtras {
    public IReadOnlyList<IssueComment> IssueComments { get; set; } = new List<IssueComment>();
    public ReviewHistorySnapshot ReviewHistory { get; set; } = new();
    public string ReviewHistorySection { get; set; } = string.Empty;
    public string CiContextSection { get; set; } = string.Empty;
    public string IssueCommentsSection { get; set; } = string.Empty;
    public string ReviewCommentsSection { get; set; } = string.Empty;
    public string ReviewThreadsSection { get; set; } = string.Empty;
    public string RelatedPrsSection { get; set; } = string.Empty;
    public IReadOnlyList<PullRequestReviewThread> ReviewThreads { get; set; } = new List<PullRequestReviewThread>();
    public AutoResolvePermissionDiagnostics StaleThreadAutoResolvePermissions { get; set; } = AutoResolvePermissionDiagnostics.Empty;
}
