using System;
using System.Collections.Generic;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Controls which notification threads are returned for the authenticated GitHub user.
/// </summary>
public sealed class GitHubNotificationQuery {
    /// <summary>
    /// Gets or sets whether read threads are included. The default returns only unread threads.
    /// </summary>
    public bool IncludeRead { get; set; }

    /// <summary>
    /// Gets or sets whether results are limited to threads in which the user is participating or mentioned.
    /// </summary>
    public bool ParticipatingOnly { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of threads to return across all GitHub pages.
    /// Use zero, the default, to return the complete matching inbox.
    /// </summary>
    public int Limit { get; set; }
}

/// <summary>
/// Represents one notification thread from the authenticated user's GitHub inbox.
/// </summary>
public sealed class GitHubNotificationThread {
    /// <summary>
    /// Initializes a notification thread.
    /// </summary>
    public GitHubNotificationThread(
        string id,
        string repositoryNameWithOwner,
        string title,
        string subjectType,
        string reason,
        bool unread,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? lastReadAtUtc,
        string repositoryUrl,
        string openUrl) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        RepositoryNameWithOwner = repositoryNameWithOwner ?? throw new ArgumentNullException(nameof(repositoryNameWithOwner));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        SubjectType = subjectType ?? throw new ArgumentNullException(nameof(subjectType));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        Unread = unread;
        UpdatedAtUtc = updatedAtUtc;
        LastReadAtUtc = lastReadAtUtc;
        RepositoryUrl = repositoryUrl ?? throw new ArgumentNullException(nameof(repositoryUrl));
        OpenUrl = openUrl ?? throw new ArgumentNullException(nameof(openUrl));
    }

    /// <summary>Gets the GitHub notification thread identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the repository in owner/name form.</summary>
    public string RepositoryNameWithOwner { get; }

    /// <summary>Gets the notification subject title.</summary>
    public string Title { get; }

    /// <summary>Gets the GitHub subject type, such as PullRequest, Issue, or CheckSuite.</summary>
    public string SubjectType { get; }

    /// <summary>Gets the GitHub reason code, such as mention, review_requested, or subscribed.</summary>
    public string Reason { get; }

    /// <summary>Gets whether the thread is unread.</summary>
    public bool Unread { get; }

    /// <summary>Gets when GitHub last updated the thread.</summary>
    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>Gets when the authenticated user last read the thread, when known.</summary>
    public DateTimeOffset? LastReadAtUtc { get; }

    /// <summary>Gets the repository's browser URL.</summary>
    public string RepositoryUrl { get; }

    /// <summary>Gets the best browser URL available for opening the notification subject.</summary>
    public string OpenUrl { get; }

    /// <summary>Gets whether the reason normally represents a direct action or attention request.</summary>
    public bool IsActionRequired => GitHubNotificationReasonClassifier.IsActionRequired(Reason);
}

/// <summary>
/// Contains a cached, polling-aware snapshot of GitHub notification threads.
/// </summary>
public sealed class GitHubNotificationSnapshot {
    /// <summary>
    /// Initializes a notification snapshot.
    /// </summary>
    public GitHubNotificationSnapshot(
        IReadOnlyList<GitHubNotificationThread> threads,
        DateTimeOffset checkedAtUtc,
        TimeSpan recommendedPollInterval,
        int? rateLimitRemaining,
        bool hasMore = false) {
        Threads = threads ?? throw new ArgumentNullException(nameof(threads));
        CheckedAtUtc = checkedAtUtc;
        RecommendedPollInterval = recommendedPollInterval;
        RateLimitRemaining = rateLimitRemaining;
        HasMore = hasMore;
    }

    /// <summary>Gets notification threads ordered by GitHub.</summary>
    public IReadOnlyList<GitHubNotificationThread> Threads { get; }

    /// <summary>Gets when the service last checked GitHub.</summary>
    public DateTimeOffset CheckedAtUtc { get; }

    /// <summary>Gets GitHub's minimum recommended polling interval.</summary>
    public TimeSpan RecommendedPollInterval { get; }

    /// <summary>Gets the remaining REST rate limit reported by GitHub, when available.</summary>
    public int? RateLimitRemaining { get; }

    /// <summary>Gets whether GitHub reported another page beyond the returned finite result.</summary>
    public bool HasMore { get; }
}

/// <summary>
/// Classifies GitHub notification reasons into compact product-facing groups.
/// </summary>
public static class GitHubNotificationReasonClassifier {
    /// <summary>
    /// Returns whether a GitHub reason normally asks the authenticated user to act or pay direct attention.
    /// </summary>
    public static bool IsActionRequired(string? reason) {
        return string.Equals(reason, "approval_requested", StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "assign", StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "ci_activity", StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "mention", StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "review_requested", StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "security_alert", StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "team_mention", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Represents a failed GitHub notifications API request without exposing credentials or response bodies.
/// </summary>
public sealed class GitHubNotificationApiException : Exception {
    /// <summary>
    /// Initializes an API exception.
    /// </summary>
    public GitHubNotificationApiException(int statusCode, string message) : base(message) {
        StatusCode = statusCode;
    }

    /// <summary>Gets the HTTP status code returned by GitHub.</summary>
    public int StatusCode { get; }
}
