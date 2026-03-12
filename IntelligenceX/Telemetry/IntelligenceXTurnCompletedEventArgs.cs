using System;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.Telemetry;

/// <summary>
/// Event arguments raised when an IntelligenceX chat turn completes.
/// </summary>
public sealed class IntelligenceXTurnCompletedEventArgs : EventArgs {
    /// <summary>
    /// Initializes a new turn-completed event args instance.
    /// </summary>
    public IntelligenceXTurnCompletedEventArgs(
        string threadId,
        string model,
        OpenAITransportKind transportKind,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? workingDirectory,
        string? workspace,
        string? feature,
        string? surface,
        TurnInfo? turn,
        bool success,
        Exception? error = null) {
        if (string.IsNullOrWhiteSpace(threadId)) {
            throw new ArgumentException("Thread id is required.", nameof(threadId));
        }
        if (string.IsNullOrWhiteSpace(model)) {
            throw new ArgumentException("Model is required.", nameof(model));
        }

        ThreadId = threadId;
        Model = model;
        TransportKind = transportKind;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Duration = completedAtUtc - startedAtUtc;
        WorkingDirectory = NormalizeOptional(workingDirectory);
        Workspace = NormalizeOptional(workspace);
        Feature = NormalizeOptional(feature);
        Surface = NormalizeOptional(surface);
        Turn = turn;
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Gets the thread identifier associated with the turn.
    /// </summary>
    public string ThreadId { get; }

    /// <summary>
    /// Gets the resolved model used for the turn.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the underlying transport kind.
    /// </summary>
    public OpenAITransportKind TransportKind { get; }

    /// <summary>
    /// Gets the UTC timestamp when the turn started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>
    /// Gets the UTC timestamp when the turn completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the elapsed duration for the turn.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the resolved working directory for the turn, when available.
    /// </summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// Gets the resolved workspace for the turn, when available.
    /// </summary>
    public string? Workspace { get; }

    /// <summary>
    /// Gets the optional telemetry feature label.
    /// </summary>
    public string? Feature { get; }

    /// <summary>
    /// Gets the optional telemetry surface label.
    /// </summary>
    public string? Surface { get; }

    /// <summary>
    /// Gets the resulting turn payload when the request completed far enough to produce one.
    /// </summary>
    public TurnInfo? Turn { get; }

    /// <summary>
    /// Gets a value indicating whether the turn completed successfully.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the exception raised when the turn fails.
    /// </summary>
    public Exception? Error { get; }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
