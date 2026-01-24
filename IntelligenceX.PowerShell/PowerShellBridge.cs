using System;
using System.Threading;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

public static class PowerShellBridge {
    public static AppServerClient Connect(string? executablePath = null, string? arguments = null, string? workingDirectory = null) {
        var options = new AppServerOptions();
        if (!string.IsNullOrWhiteSpace(executablePath)) {
            options.ExecutablePath = executablePath;
        }
        if (!string.IsNullOrWhiteSpace(arguments)) {
            options.Arguments = arguments;
        }
        if (!string.IsNullOrWhiteSpace(workingDirectory)) {
            options.WorkingDirectory = workingDirectory;
        }

        return AppServerClient.StartAsync(options).GetAwaiter().GetResult();
    }

    public static void Initialize(AppServerClient client, string name, string title, string version) {
        var info = new ClientInfo(name, title, version);
        client.InitializeAsync(info).GetAwaiter().GetResult();
    }

    public static ChatGptLoginStart StartChatGptLogin(AppServerClient client) {
        return client.StartChatGptLoginAsync().GetAwaiter().GetResult();
    }

    public static void LoginWithApiKey(AppServerClient client, string apiKey) {
        client.LoginWithApiKeyAsync(apiKey).GetAwaiter().GetResult();
    }

    public static void WaitForLogin(AppServerClient client, string? loginId, int timeoutSeconds = 300) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        client.WaitForLoginCompletionAsync(loginId, cts.Token).GetAwaiter().GetResult();
    }

    public static AccountInfo GetAccount(AppServerClient client) {
        return client.ReadAccountAsync().GetAwaiter().GetResult();
    }

    public static ThreadInfo StartThread(AppServerClient client, string model, string? currentDirectory = null, string? approvalPolicy = null, string? sandbox = null) {
        return client.StartThreadAsync(model, currentDirectory, approvalPolicy, sandbox).GetAwaiter().GetResult();
    }

    public static TurnInfo StartTurn(AppServerClient client, string threadId, string text) {
        return client.StartTurnAsync(threadId, text).GetAwaiter().GetResult();
    }

    public static void Disconnect(AppServerClient client) {
        client.Dispose();
    }
}
