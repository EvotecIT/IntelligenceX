namespace IntelligenceX.Ledger.Contracts;

/// <summary>Closed declaration shape shared by outbox producers and HTTP admission.</summary>
public static class DeclarationValidation {
    public static bool Identifier(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    public static bool ValidScope(TaskScope? scope) => scope is null || Identifier(scope.TaskId) && scope.Generation >= 1 &&
        (scope.TurnId is null || Identifier(scope.TurnId));

    public static bool ValidBoundary(DateTimeOffset recorded, DateTimeOffset? due, DateTimeOffset? review, bool resolved = false) =>
        (due.HasValue || review.HasValue) && (due is null || due.Value.Offset == TimeSpan.Zero && (resolved || due >= recorded)) &&
        (review is null || review.Value.Offset == TimeSpan.Zero && (resolved || review >= recorded));

    public static bool Valid(WorkerDeclaration declaration, DateTimeOffset now) =>
        Identifier(declaration.Id) && declaration.Scope is not null && ValidScope(declaration.Scope) &&
        Identifier(declaration.CheckpointRef) && Identifier(declaration.ConditionRef) && Identifier(declaration.NextActor) &&
        new[] { declaration.AcceptanceBy, declaration.ResultRef, declaration.CandidateRef }.All(x => x is null || Identifier(x)) &&
        (declaration.Kind is "wait" or "operation" or "question" or "handoff") &&
        (declaration.HoldType is null or "waiting-decision" or "waiting-acceptance" or "waiting-resource" or "delivery-backpressure" or "delivery-uncertain" or "observation-gap") &&
        declaration.RecordedAtUtc.Offset == TimeSpan.Zero && declaration.RecordedAtUtc <= now.AddMinutes(2) &&
        ValidBoundary(declaration.RecordedAtUtc, declaration.DueAtUtc, declaration.ReviewAfterUtc, declaration.Resolved);
}
