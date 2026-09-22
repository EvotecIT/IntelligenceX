namespace IntelligenceX.OpenAI.Transport;

/// <summary>Local retention boundary for transports that keep conversation state in process memory.</summary>
internal interface ILocalThreadLifetime {
    void ForgetThread(string threadId);
}
