using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Options for chat requests.
/// </summary>
public sealed class ChatOptions {
    /// <summary>
    /// Initializes a new instance of <see cref="ChatOptions"/> by copying values from <paramref name="other"/>.
    /// </summary>
    /// <param name="other">Options to copy.</param>
    public ChatOptions(ChatOptions other) {
        if (other is null) {
            throw new ArgumentNullException(nameof(other));
        }

        Model = other.Model;
        Instructions = other.Instructions;
        ReasoningEffort = other.ReasoningEffort;
        ReasoningSummary = other.ReasoningSummary;
        TextVerbosity = other.TextVerbosity;
        Temperature = other.Temperature;
        WorkingDirectory = other.WorkingDirectory;
        Workspace = other.Workspace;
        // Defensive copy: caller may provide a mutable list instance.
        Tools = other.Tools is null ? null : other.Tools.ToArray();
        ToolChoice = other.ToolChoice;
        ParallelToolCalls = other.ParallelToolCalls;
        PreviousResponseId = other.PreviousResponseId;
        AllowNetwork = other.AllowNetwork;
        ApprovalPolicy = other.ApprovalPolicy;
        SandboxPolicy = other.SandboxPolicy?.Clone();
        NewThread = other.NewThread;
        MaxImageBytes = other.MaxImageBytes;
        RequireWorkspaceForFileAccess = other.RequireWorkspaceForFileAccess;
    }

    /// <summary>
    /// Initializes a new <see cref="ChatOptions"/> instance.
    /// </summary>
    public ChatOptions() { }

    /// <summary>
    /// Creates a copy of this instance.
    /// </summary>
    public ChatOptions Clone() => new(this);

    /// <summary>
    /// Model name override.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// System instructions.
    /// </summary>
    public string? Instructions { get; set; }
    /// <summary>
    /// Reasoning effort hint.
    /// </summary>
    public ReasoningEffort? ReasoningEffort { get; set; }
    /// <summary>
    /// Reasoning summary hint.
    /// </summary>
    public ReasoningSummary? ReasoningSummary { get; set; }
    /// <summary>
    /// Text verbosity hint.
    /// </summary>
    public TextVerbosity? TextVerbosity { get; set; }
    /// <summary>
    /// Sampling temperature.
    /// </summary>
    public double? Temperature { get; set; }
    /// <summary>
    /// Working directory for file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Workspace path for tool access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Tool definitions available to the model.
    /// </summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; set; }
    /// <summary>
    /// Tool choice override.
    /// </summary>
    public ToolChoice? ToolChoice { get; set; }
    /// <summary>
    /// Whether tool calls can run in parallel when supported.
    /// </summary>
    public bool? ParallelToolCalls { get; set; }
    /// <summary>
    /// Previous response id for continuing a response chain.
    /// </summary>
    public string? PreviousResponseId { get; set; }
    /// <summary>
    /// Whether network access is allowed.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Approval policy string passed to the app-server.
    /// </summary>
    public string? ApprovalPolicy { get; set; }
    /// <summary>
    /// Sandbox policy for the request.
    /// </summary>
    public SandboxPolicy? SandboxPolicy { get; set; }
    /// <summary>
    /// Whether to force a new thread.
    /// </summary>
    public bool NewThread { get; set; }
    /// <summary>
    /// Maximum allowed image size in bytes.
    /// </summary>
    public long? MaxImageBytes { get; set; }
    /// <summary>
    /// Whether a workspace is required for file access.
    /// </summary>
    public bool RequireWorkspaceForFileAccess { get; set; }
}
