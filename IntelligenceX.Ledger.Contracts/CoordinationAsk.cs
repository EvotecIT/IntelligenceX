using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Limits for asks between agents. Every value is read by the service and the machine agent services at use,
/// so changing one needs no rebuild. Minutes are elapsed wall-clock time.</summary>
public sealed record AskPolicy {
    /// <summary>Routing key used when an ask names no provider; it selects an entry of <see cref="CoordinationPolicy.Routing"/>.</summary>
    public string DefaultKind { get; set; } = "engine";
    /// <summary>How long a question may wait for an answer before the helper is told to continue without one.</summary>
    public double QuestionWaitMinutes { get; set; } = 1440;
    /// <summary>Questions one request may raise before the helper must finish with what it has.</summary>
    public int MaximumQuestions { get; set; } = 8;
    /// <summary>How long a queued ask may wait for a machine before the requester is told.</summary>
    public double AdmissionMinutes { get; set; } = 15;
    /// <summary>How long one helper turn may run before it is treated as stuck and interrupted.</summary>
    public double TurnMinutes { get; set; } = 60;
    /// <summary>How many times an interrupted or failed helper turn is resumed in the same session before the request fails.</summary>
    public int ResumeAttempts { get; set; } = 1;
    /// <summary>How long a delivering host follows a woken session's turn to record its token use.</summary>
    public double WakeUsageMinutes { get; set; } = 30;
    /// <summary>Codex model and reasoning effort for helper threads. Null uses the machine's Codex default.</summary>
    public string? CodexModel { get; set; }
    public string? CodexEffort { get; set; }
    /// <summary>Model for the one-shot <c>claude -p</c> that only sends a message with SendMessage.</summary>
    public string ClaudeDeliveryModel { get; set; } = "haiku";
    /// <summary>The permission mode of that one-shot sender. "default" keeps the Claude desktop app's hold on a message from a
    /// session in another mode (its user approves it), and such a delivery is recorded as uncertain. "recipient" runs the
    /// sender in each session's recorded mode, so a delivery starts a turn at once, including in sessions that run with
    /// permissions bypassed; set it only where every session that can deliver through this Ledger is trusted.</summary>
    public string ClaudeSenderMode { get; set; } = "default";
    /// <summary>How long a delivery may stay in sending before the service marks it uncertain and tells the requester
    /// where to read the result.</summary>
    public double DeliveryUnconfirmedMinutes { get; set; } = 30;
    /// <summary>While the Claude CLI is at its usage limit, one held delivery is tried this often to see if it has passed.</summary>
    public double UsageLimitProbeMinutes { get; set; } = 15;
    /// <summary>How many more times a delivery is sent to a Claude session SendMessage couldn't reach, and how long apart;
    /// after that it waits for the session's next turn (inbox hook).</summary>
    public int UnreachableRetries { get; set; } = 1;
    public double UnreachableRetryMinutes { get; set; } = 30;
    /// <summary>How long a wake waits for its pull request's checks before it is delivered as expired.</summary>
    public double WakeExpiryHours { get; set; } = 72;
    /// <summary>Waiting wakes one session client may hold.</summary>
    public int MaximumWakes { get; set; } = 64;
}

/// <summary>One step from a session with no goal: "do this, on that machine, with that provider". The service creates the
/// goal and the help request together. The asking session is the return destination and must be on the caller's machine.
/// Source fields describe the asker's repository; the helper works in its own empty workspace. ReturnAddress is how the
/// asking session is reached when its provider addresses sessions by name: for Claude Code, the "name [ref]" that
/// ListAgents shows for the session itself. Continues names an earlier Codex ask of the same requester whose helper
/// conversation this one carries on from; it runs on the same machine. Model and Effort override the policy's Codex
/// defaults for this ask. Output "review" asks for a structured review verdict instead of free text.</summary>
public sealed record CoordinationAsk(string Id, string Task, string Project, string Branch, string Commit,
    string ReturnProvider, string ReturnSessionId, string? MachineId = null, string? Provider = null,
    string? Kind = null, string? Capability = null, string? ReturnAddress = null, string? Continues = null,
    string? Model = null, string? Effort = null, string? Output = null) {
    /// <summary>Optional short title visible to Ledger work readers.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayTitle { get; init; }
}

/// <summary>How a help request's helper runs. Model and Effort override the policy's defaults. Output is "text" (the
/// default) or "review" (a structured verdict with findings and checks). ForkSessionId is the earlier helper session
/// whose conversation a follow-up continues in a forked copy, and ContinuesRequestId the request that ran it.</summary>
public sealed record CoordinationHelperOptions(string? Model = null, string? Effort = null, string? Output = null,
    string? ContinuesRequestId = null, string? ForkSessionId = null);

/// <summary>A question from a helper or a notice from the service to the requester. Notices need no answer.
/// State is asked, answered, expired or resumed; Delivery carries the requester-side send, AnswerDelivery the
/// helper-side send. A sending or uncertain delivery is never repeated.</summary>
public sealed record CoordinationQuestion(string RequestId, long Generation, int Number, string Kind, string Text,
    DateTimeOffset AskedAtUtc, string State = "asked", string? Answer = null, DateTimeOffset? AnsweredAtUtc = null,
    CoordinationDelivery? Delivery = null, CoordinationDelivery? AnswerDelivery = null, long Order = 0);

/// <summary>One send through a supported client. Stage is sending, submitted, uncertain or held.</summary>
public sealed record CoordinationDelivery(string AttemptId, string Stage, string Host, string? EvidenceRef = null,
    DateTimeOffset? AtUtc = null);

/// <summary>A helper's question for its requester, raised by the host running the helper for its exact grant.</summary>
public sealed record CoordinationQuestionCommand(long Generation, string GrantId, string BootId, string Text);

/// <summary>The requester's answer to one question.</summary>
public sealed record CoordinationAnswerCommand(string Text);

/// <summary>Starts (Stage "sending") or records (submitted, uncertain, held, not-submitted) one question-side or
/// answer-side delivery. Target is "question" for the requester side and "answer" for the helper side.</summary>
public sealed record CoordinationQuestionDeliveryCommand(string Target, string AttemptId, string Stage, string? EvidenceRef = null);

/// <summary>One question, notice or answer that the calling host has to deliver. Target "question" goes to the requester
/// (ReturnTo); target "answer" goes into the helper session this host runs.</summary>
public sealed record CoordinationQuestionItem(CoordinationQuestion Question, string Target, CoordinationReturnBinding ReturnTo,
    string? HelperMachine, string? HelperProvider);

/// <summary>One bounded page of questions, notices and answers that the caller has to deliver.</summary>
public sealed record CoordinationQuestionPage(CoordinationQuestionItem[] Items, long? NextOrder);

/// <summary>Tokens spent on behalf of one request. Kind is worker (a helper turn), wake (a woken requester turn) or
/// delivery (the one-shot sender for a Claude session). CostUsd is set only when the provider reports it.</summary>
public sealed record CoordinationUsage(string Kind, string Provider, string Model, long InputTokens, long CachedInputTokens,
    long OutputTokens, long TotalTokens, string MachineId, DateTimeOffset AtUtc, double? CostUsd = null, string? SessionId = null);

/// <summary>Usage recorded for one request, and totals for a day by request, kind, provider and model.</summary>
public sealed record CoordinationUsageView(string RequestId, CoordinationUsage[] Items);
public sealed record CoordinationUsageTotal(string Key, int Count, long InputTokens, long CachedInputTokens,
    long OutputTokens, long TotalTokens, double CostUsd);
public sealed record CoordinationUsageReport(string Date, CoordinationUsageTotal[] ByRequest, CoordinationUsageTotal[] ByKind);
