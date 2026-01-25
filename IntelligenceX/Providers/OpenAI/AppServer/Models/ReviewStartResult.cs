namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ReviewStartResult {
    public ReviewStartResult(TurnInfo turn, string? reviewThreadId) {
        Turn = turn;
        ReviewThreadId = reviewThreadId;
    }

    public TurnInfo Turn { get; }
    public string? ReviewThreadId { get; }
}
