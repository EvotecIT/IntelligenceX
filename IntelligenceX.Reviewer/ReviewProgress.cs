namespace IntelligenceX.Reviewer;

internal enum ReviewProgressState {
    Pending,
    InProgress,
    Complete
}

internal enum ReviewProgressStage {
    Context,
    Files,
    Review,
    Finalize
}

internal sealed class ReviewProgress {
    public ReviewProgressState Context { get; set; } = ReviewProgressState.Pending;
    public ReviewProgressState Files { get; set; } = ReviewProgressState.Pending;
    public ReviewProgressState Review { get; set; } = ReviewProgressState.Pending;
    public ReviewProgressState Finalize { get; set; } = ReviewProgressState.Pending;
    public string? StatusLine { get; set; }
}
