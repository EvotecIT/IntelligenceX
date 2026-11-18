namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Creates the goal and help request for one ask in a single call.</summary>
    public Task<CoordinationWork> AskAsync(CoordinationAsk ask, CancellationToken token = default) =>
        PostAsync("ask", ask, LedgerReadJson.Default.CoordinationAsk, LedgerReadJson.Default.CoordinationWork, token);

    /// <summary>Asks for this session to be woken once: when a pull request's checks finish, or after a delay.</summary>
    public Task<CoordinationWake> WakeAsync(CoordinationWakeRequest wake, CancellationToken token = default) =>
        PostAsync("wakes", wake, LedgerReadJson.Default.CoordinationWakeRequest, LedgerReadJson.Default.CoordinationWake, token);

    /// <summary>Reads one wake (its owner, the delivering host or an operator).</summary>
    public Task<CoordinationWake> WakeStatusAsync(string id, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/wakes/" + Uri.EscapeDataString(id), LedgerReadJson.Default.CoordinationWake, token);

    /// <summary>Cancels a wake that has not been delivered (its owner or an operator).</summary>
    public Task<CoordinationWake> CancelWakeAsync(string id, CancellationToken token = default) =>
        PostAsync("wakes/" + Uri.EscapeDataString(id) + "/cancel", new CoordinationWakeDeliveryCommand("cancel", "cancel"),
            LedgerReadJson.Default.CoordinationWakeDeliveryCommand, LedgerReadJson.Default.CoordinationWake, token);

    /// <summary>Reads the due wakes this host has to deliver, optionally only one session's.</summary>
    public Task<CoordinationWakePage> WakesAsync(CancellationToken token = default, long afterOrder = 0, string? sessionId = null) =>
        GetAsync("api/coordination/v1/wakes?afterOrder=" + afterOrder.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (sessionId is null ? "" : "&sessionId=" + Uri.EscapeDataString(sessionId)), LedgerReadJson.Default.CoordinationWakePage, token);

    /// <summary>Starts or records one wake delivery (host).</summary>
    public Task<CoordinationWake> WakeDeliveryAsync(string id, CoordinationWakeDeliveryCommand command, CancellationToken token = default) =>
        PostAsync("wakes/" + Uri.EscapeDataString(id) + "/delivery", command,
            LedgerReadJson.Default.CoordinationWakeDeliveryCommand, LedgerReadJson.Default.CoordinationWake, token);

    /// <summary>Raises a helper's question for its exact grant (host).</summary>
    public Task<CoordinationQuestion> RaiseQuestionAsync(string id, CoordinationQuestionCommand command, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/questions", command,
            LedgerReadJson.Default.CoordinationQuestionCommand, LedgerReadJson.Default.CoordinationQuestion, token);

    /// <summary>Answers one open question (original requester).</summary>
    public Task<CoordinationQuestion> AnswerAsync(string id, int number, CoordinationAnswerCommand command, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/questions/" + Number(number) + "/answer", command,
            LedgerReadJson.Default.CoordinationAnswerCommand, LedgerReadJson.Default.CoordinationQuestion, token);

    /// <summary>Starts or records one question-side or answer-side delivery (host).</summary>
    public Task<CoordinationQuestion> QuestionDeliveryAsync(string id, int number, CoordinationQuestionDeliveryCommand command,
        CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/questions/" + Number(number) + "/delivery", command,
            LedgerReadJson.Default.CoordinationQuestionDeliveryCommand, LedgerReadJson.Default.CoordinationQuestion, token);

    /// <summary>Reads the questions, notices and answers this host has to deliver.</summary>
    public Task<CoordinationQuestionPage> QuestionsAsync(CancellationToken token = default, long afterOrder = 0) =>
        GetAsync("api/coordination/v1/questions?afterOrder=" + afterOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LedgerReadJson.Default.CoordinationQuestionPage, token);

    /// <summary>Records tokens spent for one request (host).</summary>
    public Task<CoordinationUsageView> RecordUsageAsync(string id, CoordinationUsage usage, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/usage", usage,
            LedgerReadJson.Default.CoordinationUsage, LedgerReadJson.Default.CoordinationUsageView, token);

    /// <summary>Reads the tokens recorded for one request.</summary>
    public Task<CoordinationUsageView> UsageAsync(string id, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/requests/" + Uri.EscapeDataString(id) + "/usage", LedgerReadJson.Default.CoordinationUsageView, token);

    /// <summary>Reads token totals for one UTC day.</summary>
    public Task<CoordinationUsageReport> UsageReportAsync(DateOnly day, CancellationToken token = default) =>
        GetAsync("api/coordination/v1/usage?date=" + day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            LedgerReadJson.Default.CoordinationUsageReport, token);
}
