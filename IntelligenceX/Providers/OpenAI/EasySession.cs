using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI;

/// <summary>
/// High-level convenience wrapper for ChatGPT sessions.
/// </summary>
/// <example>
/// <code>
/// var session = await EasySession.StartAsync();
/// var result = await session.AskAsync("Summarize this PR");
/// Console.WriteLine(result.Text);
/// </code>
/// </example>
public sealed class EasySession : IDisposable
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    private readonly IntelligenceXClient _client;
    private readonly EasySessionOptions _options;
    private bool _loggedIn;

    private EasySession(IntelligenceXClient client, EasySessionOptions options) {
        _client = client;
        _options = options;
    }

    /// <summary>
    /// Underlying client instance used by the session.
    /// </summary>
    public IntelligenceXClient Client => _client;

    /// <summary>
    /// Starts a new session and optionally logs in (based on <see cref="EasySessionOptions.AutoLogin"/>).
    /// </summary>
    public static async Task<EasySession> StartAsync(EasySessionOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new EasySessionOptions();
        options.Validate();
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

    /// <summary>
    /// Sends a text-only chat request.
    /// </summary>
    public Task<TurnInfo> ChatAsync(string text, CancellationToken cancellationToken = default) {
        return ChatAsync(ChatInput.FromText(text), null, cancellationToken);
    }

    /// <summary>
    /// Sends a text-only request and returns a simplified response.
    /// </summary>
    public async Task<EasyChatResult> AskAsync(string text, EasyChatOptions? options = null, CancellationToken cancellationToken = default) {
        var turn = await ChatAsync(ChatInput.FromText(text), options, cancellationToken).ConfigureAwait(false);
        return EasyChatResult.FromTurn(turn);
    }

    /// <summary>
    /// Sends a text request with an image from a local path.
    /// </summary>
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

    /// <summary>
    /// Sends a fully constructed input payload.
    /// </summary>
    public async Task<TurnInfo> ChatAsync(ChatInput input, EasyChatOptions? options = null, CancellationToken cancellationToken = default) {
        await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);

        var resolvedWorkspace = options?.Workspace ?? _options.Workspace;
        var maxImageBytes = options?.MaxImageBytes ?? _options.MaxImageBytes;
        var requireWorkspace = options?.RequireWorkspaceForFileAccess ?? _options.RequireWorkspaceForFileAccess;
        EnsureFileSafety(input, resolvedWorkspace, maxImageBytes, requireWorkspace);

        var chatOptions = new ChatOptions();
        if (options is not null) {
            chatOptions.Model = options.Model;
            chatOptions.Instructions = options.Instructions;
            chatOptions.ReasoningEffort = options.ReasoningEffort;
            chatOptions.ReasoningSummary = options.ReasoningSummary;
            chatOptions.TextVerbosity = options.TextVerbosity;
            chatOptions.Temperature = options.Temperature;
            chatOptions.NewThread = options.NewThread;
            chatOptions.Workspace = options.Workspace;
            chatOptions.AllowNetwork = options.AllowNetwork;
            chatOptions.MaxImageBytes = options.MaxImageBytes;
            if (options.RequireWorkspaceForFileAccess.HasValue) {
                chatOptions.RequireWorkspaceForFileAccess = options.RequireWorkspaceForFileAccess.Value;
            }
        }

        return await _client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureLoggedInAsync(CancellationToken cancellationToken = default) {
        if (_options.ValidateLoginOnEachRequest && _loggedIn) {
            if (!await TryReadAccountAsync(cancellationToken).ConfigureAwait(false)) {
                _loggedIn = false;
            }
        }

        if (_loggedIn || _options.Login == EasyLoginMode.None) {
            return;
        }

        if (await TryReadAccountAsync(cancellationToken).ConfigureAwait(false)) {
            _loggedIn = true;
            return;
        }

        switch (_options.Login) {
            case EasyLoginMode.ApiKey:
                if (_options.TransportKind == OpenAITransportKind.Native) {
                    throw new InvalidOperationException("API key login is not supported with native ChatGPT transport.");
                }
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
            DefaultApprovalPolicy = options.ApprovalPolicy,
            TransportKind = options.TransportKind
        };

        clientOptions.NativeOptions.AuthStore = options.NativeOptions.AuthStore;
        clientOptions.NativeOptions.Originator = options.NativeOptions.Originator;
        clientOptions.NativeOptions.ResponsesUrl = options.NativeOptions.ResponsesUrl;
        clientOptions.NativeOptions.ModelUrls = options.NativeOptions.ModelUrls;
        clientOptions.NativeOptions.ClientVersion = options.NativeOptions.ClientVersion;
        clientOptions.NativeOptions.Instructions = options.NativeOptions.Instructions;
        clientOptions.NativeOptions.ReasoningEffort = options.NativeOptions.ReasoningEffort;
        clientOptions.NativeOptions.ReasoningSummary = options.NativeOptions.ReasoningSummary;
        clientOptions.NativeOptions.TextVerbosity = options.NativeOptions.TextVerbosity;
        clientOptions.NativeOptions.IncludeReasoningEncryptedContent = options.NativeOptions.IncludeReasoningEncryptedContent;
        clientOptions.NativeOptions.OAuthTimeout = options.NativeOptions.OAuthTimeout;
        clientOptions.NativeOptions.UseLocalListener = options.NativeOptions.UseLocalListener;
        clientOptions.NativeOptions.PersistCodexAuthJson = options.NativeOptions.PersistCodexAuthJson;
        clientOptions.NativeOptions.CodexHome = options.NativeOptions.CodexHome;
        clientOptions.NativeOptions.UserAgent = options.NativeOptions.UserAgent;
        clientOptions.NativeOptions.OAuth.AuthorizeUrl = options.NativeOptions.OAuth.AuthorizeUrl;
        clientOptions.NativeOptions.OAuth.TokenUrl = options.NativeOptions.OAuth.TokenUrl;
        clientOptions.NativeOptions.OAuth.ClientId = options.NativeOptions.OAuth.ClientId;
        clientOptions.NativeOptions.OAuth.Scopes = options.NativeOptions.OAuth.Scopes;
        clientOptions.NativeOptions.OAuth.RedirectUri = options.NativeOptions.OAuth.RedirectUri;
        clientOptions.NativeOptions.OAuth.RedirectPort = options.NativeOptions.OAuth.RedirectPort;
        clientOptions.NativeOptions.OAuth.RedirectPath = options.NativeOptions.OAuth.RedirectPath;
        clientOptions.NativeOptions.OAuth.AddOrganizations = options.NativeOptions.OAuth.AddOrganizations;
        clientOptions.NativeOptions.OAuth.CodexCliSimplifiedFlow = options.NativeOptions.OAuth.CodexCliSimplifiedFlow;
        clientOptions.NativeOptions.OAuth.Originator = options.NativeOptions.OAuth.Originator;

        if (options.TransportKind == OpenAITransportKind.AppServer) {
            clientOptions.AppServerOptions.ExecutablePath = options.AppServerOptions.ExecutablePath;
            clientOptions.AppServerOptions.Arguments = options.AppServerOptions.Arguments;
            clientOptions.AppServerOptions.WorkingDirectory = options.AppServerOptions.WorkingDirectory;
            clientOptions.AppServerOptions.RedirectStandardError = options.AppServerOptions.RedirectStandardError;
            foreach (var pair in options.AppServerOptions.Environment) {
                clientOptions.AppServerOptions.Environment[pair.Key] = pair.Value;
            }
        }

        return clientOptions;
    }

    private async Task LoginChatGptAsync(CancellationToken cancellationToken) {
        void HandleUrl(string url) {
            if (_options.OnLoginUrl is not null) {
                _options.OnLoginUrl(url);
            } else if (_options.PrintLoginUrl) {
                Console.WriteLine(url);
            }
            if (_options.OpenBrowser) {
                TryOpenUrl(url);
            }
        }

        await _client.LoginChatGptAndWaitAsync(HandleUrl, _options.OnPrompt, _options.UseLocalListener,
            _options.NativeOptions.OAuthTimeout, cancellationToken).ConfigureAwait(false);
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

    private static void EnsureFileSafety(ChatInput input, string? workspace, long maxImageBytes, bool requireWorkspace) {
        var paths = input.GetImagePaths();
        if (paths.Length == 0) {
            return;
        }
        foreach (var path in paths) {
            PathSafety.EnsureFileExists(path);
            PathSafety.EnsureMaxFileSize(path, maxImageBytes);
            if (requireWorkspace) {
                if (string.IsNullOrWhiteSpace(workspace)) {
                    throw new InvalidOperationException("Workspace is required for file access.");
                }
                PathSafety.EnsureUnderRoot(path, workspace!);
            }
        }
    }

    public void Dispose() {
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
#else
        _client.DisposeAsync().GetAwaiter().GetResult();
#endif
    }

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    public ValueTask DisposeAsync() => _client.DisposeAsync();
#else
    public Task DisposeAsync() => _client.DisposeAsync();
#endif
}
