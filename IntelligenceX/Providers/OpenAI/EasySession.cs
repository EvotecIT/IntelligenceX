using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

public sealed class EasySession : IAsyncDisposable {
    private readonly IntelligenceXClient _client;
    private readonly EasySessionOptions _options;
    private bool _loggedIn;

    private EasySession(IntelligenceXClient client, EasySessionOptions options) {
        _client = client;
        _options = options;
    }

    public IntelligenceXClient Client => _client;

    public static async Task<EasySession> StartAsync(EasySessionOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new EasySessionOptions();
        var clientOptions = BuildClientOptions(options);
        var client = await IntelligenceXClient.ConnectAsync(clientOptions, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(options.Workspace)) {
            client.ConfigureWorkspace(options.Workspace!, options.AllowNetwork);
        }

        var session = new EasySession(client, options);
        if (options.AutoLogin) {
            await session.EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);
        }
        return session;
    }

    public IDisposable SubscribeDelta(Action<string> onDelta) => _client.SubscribeDelta(onDelta);

    public Task<TurnInfo> ChatAsync(string text, CancellationToken cancellationToken = default) {
        return ChatAsync(ChatInput.FromText(text), null, cancellationToken);
    }

    public async Task<EasyChatResult> AskAsync(string text, EasyChatOptions? options = null, CancellationToken cancellationToken = default) {
        var turn = await ChatAsync(ChatInput.FromText(text), options, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    public async Task<EasyChatResult> AskWithImagePathAsync(string text, string imagePath, EasyChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        var input = ChatInput.FromTextWithImagePath(text, imagePath);
        var turn = await ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    public async Task<EasyChatResult> AskWithImageUrlAsync(string text, string imageUrl, EasyChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        var input = ChatInput.FromTextWithImageUrl(text, imageUrl);
        var turn = await ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    public Task<TurnInfo> ChatWithImagePathAsync(string text, string imagePath, EasyChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        var input = ChatInput.FromTextWithImagePath(text, imagePath);
        return ChatAsync(input, options, cancellationToken);
    }

    public Task<TurnInfo> ChatWithImageUrlAsync(string text, string imageUrl, EasyChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        var input = ChatInput.FromTextWithImageUrl(text, imageUrl);
        return ChatAsync(input, options, cancellationToken);
    }

    public async Task<TurnInfo> ChatAsync(ChatInput input, EasyChatOptions? options = null, CancellationToken cancellationToken = default) {
        await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);

        var chatOptions = new ChatOptions();
        if (options is not null) {
            chatOptions.Model = options.Model;
            chatOptions.NewThread = options.NewThread;
            chatOptions.Workspace = options.Workspace;
            chatOptions.AllowNetwork = options.AllowNetwork;
        }

        return await _client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureLoggedInAsync(CancellationToken cancellationToken = default) {
        if (_loggedIn || _options.Login == EasyLoginMode.None) {
            return;
        }

        if (await TryReadAccountAsync(cancellationToken).ConfigureAwait(false)) {
            _loggedIn = true;
            return;
        }

        switch (_options.Login) {
            case EasyLoginMode.ApiKey:
                var key = _options.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrWhiteSpace(key)) {
                    throw new InvalidOperationException("API key is required for ApiKey login.");
                }
                await _client.LoginApiKeyAsync(key, cancellationToken).ConfigureAwait(false);
                break;
            case EasyLoginMode.ChatGpt:
                await LoginChatGptAsync(cancellationToken).ConfigureAwait(false);
                break;
        }

        _loggedIn = true;
    }

    private async Task<bool> TryReadAccountAsync(CancellationToken cancellationToken) {
        try {
            await _client.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            return true;
        } catch {
            return false;
        }
    }

    private static IntelligenceXClientOptions BuildClientOptions(EasySessionOptions options) {
        var clientOptions = new IntelligenceXClientOptions {
            ClientInfo = options.ClientInfo,
            AutoInitialize = options.AutoInitialize,
            DefaultModel = options.DefaultModel,
            DefaultWorkingDirectory = options.WorkingDirectory,
            DefaultApprovalPolicy = options.ApprovalPolicy
        };

        clientOptions.AppServerOptions.ExecutablePath = options.AppServerOptions.ExecutablePath;
        clientOptions.AppServerOptions.Arguments = options.AppServerOptions.Arguments;
        clientOptions.AppServerOptions.WorkingDirectory = options.AppServerOptions.WorkingDirectory;
        clientOptions.AppServerOptions.RedirectStandardError = options.AppServerOptions.RedirectStandardError;
        foreach (var pair in options.AppServerOptions.Environment) {
            clientOptions.AppServerOptions.Environment[pair.Key] = pair.Value;
        }

        return clientOptions;
    }

    private async Task LoginChatGptAsync(CancellationToken cancellationToken) {
        var login = await _client.LoginChatGptAsync(cancellationToken).ConfigureAwait(false);
        if (_options.OnLoginUrl is not null) {
            _options.OnLoginUrl(login.AuthUrl);
        } else if (_options.PrintLoginUrl) {
            Console.WriteLine(login.AuthUrl);
        }

        if (_options.OpenBrowser) {
            TryOpenUrl(login.AuthUrl);
        }
        await _client.RawClient.WaitForLoginCompletionAsync(login.LoginId, cancellationToken).ConfigureAwait(false);
    }

    private static void TryOpenUrl(string url) {
        try {
            var psi = new ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        } catch {
            // Ignore failures to open browser.
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
