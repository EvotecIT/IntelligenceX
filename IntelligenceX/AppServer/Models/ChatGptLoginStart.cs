namespace IntelligenceX.AppServer.Models;

public sealed class ChatGptLoginStart {
    public ChatGptLoginStart(string loginId, string authUrl) {
        LoginId = loginId;
        AuthUrl = authUrl;
    }

    public string LoginId { get; }
    public string AuthUrl { get; }
}
