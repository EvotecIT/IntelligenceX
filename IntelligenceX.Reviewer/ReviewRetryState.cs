namespace IntelligenceX.Reviewer;

internal sealed class ReviewRetryState {
    public int LastAttempt { get; set; }
    public int MaxAttempts { get; set; }
}
