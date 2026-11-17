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
}

/// <summary>One step from a session with no goal: "do this, on that machine, with that provider". The service creates the
/// goal and the help request together. The asking session is the return destination and must be on the caller's machine.
/// Source fields describe the asker's repository; the helper works in its own empty workspace. ReturnAddress is how the
/// asking session is reached when its provider addresses sessions by name: for Claude Code, the "name [ref]" that
/// ListAgents shows for the session itself.</summary>
public sealed record CoordinationAsk(string Id, string Task, string Project, string Branch, string Commit,
    string ReturnProvider, string ReturnSessionId, string? MachineId = null, string? Provider = null,
    string? Kind = null, string? Capability = null, string? ReturnAddress = null);

/// <summary>A question from a helper or a notice from the service to the requester. Notices need no answer.
/// State is asked, answered, expired or resumed; Delivery carries the requester-side send, AnswerDelivery the
/// helper-side send. A sending or uncertain delivery is never repeated.</summary>
public sealed record CoordinationQuestion(string RequestId, long Generation, int Number, string Kind, string Text,
    DateTimeOffset AskedAtUtc, string State = "asked", string? Answer = null, DateTimeOffset? AnsweredAtUtc = null,
    CoordinationDelivery? Delivery = null, CoordinationDelivery? AnswerDelivery = null, long Order = 0);

/// <summary>One send through a supported client. Stage is sending, submitted, uncertain or held.</summary>
public sealed record CoordinationDelivery(string AttemptId, string Stage, string Host, string? EvidenceRef = null);

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
