using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Super-easy chat command that handles connect, init, login, thread, and send.</para>
/// <para type="description">Convenience entry point for quick chat: it connects, logs in (if needed), creates or
/// reuses a thread, sends input, and returns the resulting turn. Supports streaming output and
/// a simple DSL for text/image inputs.</para>
/// <example>
///  <para>Quick chat</para>
///  <code>Invoke-IntelligenceXChat -Text "Summarize these changes."</code>
/// </example>
/// <example>
///  <para>Chat with streaming output</para>
///  <code>Invoke-IntelligenceXChat -Text "List risks." -Stream -WaitSeconds 10</code>
/// </example>
/// <example>
///  <para>Use the DSL to mix text and an image</para>
///  <code>@"
/// text: Describe the image
/// image: C:\temp\diagram.png
/// "@ | Invoke-IntelligenceXChat -Dsl</code>
/// </example>
/// <example>
///  <para>Use API key login</para>
///  <code>Invoke-IntelligenceXChat -Text "Hello" -Login ApiKey -ApiKey $env:OPENAI_API_KEY</code>
/// </example>
/// <example>
///  <para>Run with a workspace sandbox and network access</para>
///  <code>Invoke-IntelligenceXChat -Text "Run tests" -Workspace "C:\repo" -AllowNetwork</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXChat", DefaultParameterSetName = "Text")]
[OutputType(typeof(TurnInfo), typeof(JsonValue))]
public sealed class CmdletInvokeIntelligenceXChat : IntelligenceXCmdlet {
    private readonly System.Collections.Generic.List<string> _pipelineInputs = new();

    /// <summary>
    /// <para type="description">Message text to send.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Pipeline input for DSL lines (text, image, url).</para>
    /// </summary>
    [Parameter(ValueFromPipeline = true, ParameterSetName = "Pipeline")]
    public string? InputObject { get; set; }

    /// <summary>
    /// <para type="description">Parse Text as a simple DSL (lines starting with image:, url:, text:).</para>
    /// </summary>
    [Parameter(ParameterSetName = "Text")]
    [Parameter(ParameterSetName = "Pipeline")]
    public SwitchParameter Dsl { get; set; }

    /// <summary>
    /// <para type="description">Model identifier. Defaults to the shared OpenAI default model.</para>
    /// </summary>
    [Parameter]
    public string Model { get; set; } = OpenAIModelCatalog.DefaultModel;

    /// <summary>
    /// <para type="description">Login method: ChatGpt, ApiKey, or None.</para>
    /// </summary>
    [Parameter]
    [ValidateSet("ChatGpt", "ApiKey", "None")]
    public string Login { get; set; } = "ChatGpt";

    /// <summary>
    /// <para type="description">API key to use when Login is ApiKey.</para>
    /// </summary>
    [Parameter]
    public string? ApiKey { get; set; }

    /// <summary>
    /// <para type="description">Open the login URL in the default browser.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter OpenBrowser { get; set; }

    /// <summary>
    /// <para type="description">Write streaming deltas to the host.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Stream { get; set; }

    /// <summary>
    /// <para type="description">Wait N seconds for streaming output after sending.</para>
    /// </summary>
    [Parameter]
    public int WaitSeconds { get; set; } = 0;

    /// <summary>
    /// <para type="description">Reset thread before sending.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter NewThread { get; set; }

    /// <summary>
    /// <para type="description">Client name for initialization.</para>
    /// </summary>
    [Parameter]
    public string ClientName { get; set; } = "IntelligenceX.PowerShell";

    /// <summary>
    /// <para type="description">Client title for initialization.</para>
    /// </summary>
    [Parameter]
    public string ClientTitle { get; set; } = "IntelligenceX PowerShell";

    /// <summary>
    /// <para type="description">Client version for initialization.</para>
    /// </summary>
    [Parameter]
    public string ClientVersion { get; set; } = "0.1.0";

    /// <summary>
    /// <para type="description">Codex executable path.</para>
    /// </summary>
    [Parameter]
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// <para type="description">Codex app-server arguments.</para>
    /// </summary>
    [Parameter]
    public string? Arguments { get; set; }

    /// <summary>
    /// <para type="description">Working directory for app-server.</para>
    /// </summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// <para type="description">Workspace directory for file writes (sets sandbox policy).</para>
    /// </summary>
    [Parameter]
    public string? Workspace { get; set; }

    /// <summary>
    /// <para type="description">Allow network access when using a workspace sandbox.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter AllowNetwork { get; set; }

    /// <summary>
    /// <para type="description">Approval policy (for example auto).</para>
    /// </summary>
    [Parameter]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// <para type="description">Image file path to include with the message.</para>
    /// </summary>
    [Parameter]
    public string? ImagePath { get; set; }

    /// <summary>
    /// <para type="description">Image URL to include with the message.</para>
    /// </summary>
    [Parameter]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// <para type="description">System instructions for the assistant.</para>
    /// </summary>
    [Parameter]
    public string? Instructions { get; set; }

    /// <summary>
    /// <para type="description">Reasoning effort level.</para>
    /// </summary>
    [Parameter]
    public ReasoningEffort? ReasoningEffort { get; set; }

    /// <summary>
    /// <para type="description">Reasoning summary level.</para>
    /// </summary>
    [Parameter]
    public ReasoningSummary? ReasoningSummary { get; set; }

    /// <summary>
    /// <para type="description">Response verbosity level.</para>
    /// </summary>
    [Parameter]
    public TextVerbosity? TextVerbosity { get; set; }

    /// <summary>
    /// <para type="description">Sampling temperature.</para>
    /// </summary>
    [Parameter]
    public double? Temperature { get; set; }

    /// <summary>
    /// <para type="description">Save image outputs to the specified directory.</para>
    /// </summary>
    [Parameter]
    public string? SaveImagesTo { get; set; }

    /// <summary>
    /// <para type="description">Download image URLs when saving images.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter DownloadImageUrls { get; set; }

    /// <summary>
    /// <para type="description">Overwrite existing files when saving images.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter OverwriteImages { get; set; }

    /// <summary>
    /// <para type="description">Prefix for saved image file names.</para>
    /// </summary>
    [Parameter]
    public string? ImageFileNamePrefix { get; set; }

    /// <summary>
    /// <para type="description">Return raw JSON response.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <inheritdoc/>
    protected override async Task ProcessRecordAsync() {
        if (ParameterSetName.Equals("Pipeline", StringComparison.OrdinalIgnoreCase)) {
            var inputObject = InputObject;
            if (!IsNullOrWhiteSpace(inputObject)) {
                _pipelineInputs.Add(inputObject!);
            }
            return;
        }

        var input = Dsl.IsPresent ? BuildInputFromDsl(Text) : BuildInput();
        await SendAsync(input).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task EndProcessingAsync() {
        if (!ParameterSetName.Equals("Pipeline", StringComparison.OrdinalIgnoreCase) || _pipelineInputs.Count == 0) {
            return;
        }

        var input = BuildInputFromPipeline();
        await SendAsync(input).ConfigureAwait(false);
    }

    private async Task SendAsync(IntelligenceX.Json.JsonArray input) {
        var client = ClientContext.DefaultClient;
        if (client is null) {
            var options = new IntelligenceXClientOptions {
                TransportKind = OpenAITransportKind.Native
            };
            if (!string.IsNullOrWhiteSpace(ExecutablePath) ||
                !string.IsNullOrWhiteSpace(Arguments) ||
                !string.IsNullOrWhiteSpace(WorkingDirectory)) {
                options.TransportKind = OpenAITransportKind.AppServer;
            }
            if (!string.IsNullOrWhiteSpace(ExecutablePath)) {
                options.AppServerOptions.ExecutablePath = ExecutablePath!;
            }
            if (!string.IsNullOrWhiteSpace(Arguments)) {
                options.AppServerOptions.Arguments = Arguments!;
            }
            if (!string.IsNullOrWhiteSpace(WorkingDirectory)) {
                options.AppServerOptions.WorkingDirectory = WorkingDirectory!;
            }
            client = await IntelligenceXClient.ConnectAsync(options, CancelToken).ConfigureAwait(false);
            SetDefaultClient(client);
        }

        if (!ClientContext.Initialized) {
            var info = new ClientInfo(ClientName, ClientTitle, ClientVersion);
            await client.InitializeAsync(info, CancelToken).ConfigureAwait(false);
            ClientContext.Initialized = true;
        }

        if (!Login.Equals("None", StringComparison.OrdinalIgnoreCase)) {
            var loggedIn = await TryReadAccountAsync(client).ConfigureAwait(false);
            if (!loggedIn) {
                if (Login.Equals("ApiKey", StringComparison.OrdinalIgnoreCase)) {
                    var key = ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    if (string.IsNullOrWhiteSpace(key)) {
                        throw new InvalidOperationException("API key is required for ApiKey login.");
                    }
                    await client.LoginApiKeyAsync(key, CancelToken).ConfigureAwait(false);
                } else {
                    await client.EnsureChatGptLoginAsync(onUrl: url => {
                        WriteVerbose($"Login URL: {url}");
                        if (OpenBrowser.IsPresent) {
                            TryOpenUrl(url);
                        }
                    }, cancellationToken: CancelToken).ConfigureAwait(false);
                }
            }
        }

        if (NewThread.IsPresent || string.IsNullOrWhiteSpace(ClientContext.DefaultThreadId)) {
            var thread = await client.StartNewThreadAsync(Model, cancellationToken: CancelToken).ConfigureAwait(false);
            ClientContext.DefaultThreadId = thread.Id;
        } else {
            await client.UseThreadAsync(ClientContext.DefaultThreadId!, CancelToken).ConfigureAwait(false);
        }

        var sandboxPolicy = BuildSandboxPolicy();
        var cwd = Workspace ?? WorkingDirectory;
        var approval = ApprovalPolicy;
        if (string.IsNullOrWhiteSpace(approval) && !string.IsNullOrWhiteSpace(Workspace)) {
            approval = "auto";
        }

        if (Raw.IsPresent) {
            var rawClient = client.RequireAppServer();
            void Handler(object? sender, IntelligenceX.Rpc.JsonRpcNotificationEventArgs args) {
                if (!Stream.IsPresent) {
                    return;
                }
                var text = args.Params?.AsObject()?.GetObject("delta")?.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    WriteObject(text);
                }
            }

            if (Stream.IsPresent) {
                rawClient.NotificationReceived += Handler;
            }
            try {
                var parameters = new JsonObject()
                    .Add("threadId", ClientContext.DefaultThreadId!)
                    .Add("input", input);
                if (!string.IsNullOrWhiteSpace(Model)) {
                    parameters.Add("model", Model);
                }
                if (!string.IsNullOrWhiteSpace(cwd)) {
                    parameters.Add("cwd", cwd);
                }
                if (!string.IsNullOrWhiteSpace(approval)) {
                    parameters.Add("approvalPolicy", approval);
                }
                if (sandboxPolicy is not null) {
                    parameters.Add("sandboxPolicy", SandboxPolicyJson.ToJson(sandboxPolicy));
                }
                var rawResult = await rawClient.CallAsync("turn/start", parameters, CancelToken).ConfigureAwait(false);
                if (WaitSeconds > 0 && Stream.IsPresent) {
                    await Task.Delay(TimeSpan.FromSeconds(WaitSeconds), CancelToken).ConfigureAwait(false);
                }
                WriteObject(rawResult);
            } finally {
                if (Stream.IsPresent) {
                    rawClient.NotificationReceived -= Handler;
                }
            }
            return;
        }

        var chatInput = BuildChatInput(input);
        var requireWorkspace = !string.IsNullOrWhiteSpace(Workspace);
        var chatOptions = new ChatOptions {
            Model = Model,
            Instructions = Instructions,
            ReasoningEffort = ReasoningEffort,
            ReasoningSummary = ReasoningSummary,
            TextVerbosity = TextVerbosity,
            Temperature = Temperature,
            WorkingDirectory = cwd,
            Workspace = Workspace,
            AllowNetwork = AllowNetwork.IsPresent,
            ApprovalPolicy = approval,
            SandboxPolicy = sandboxPolicy,
            RequireWorkspaceForFileAccess = requireWorkspace
        };

        IDisposable? subscription = null;
        if (Stream.IsPresent) {
            subscription = client.SubscribeDelta(text => {
                if (!string.IsNullOrWhiteSpace(text)) {
                    WriteObject(text);
                }
            });
        }

        try {
            var turn = await client.ChatAsync(chatInput, chatOptions, CancelToken).ConfigureAwait(false);
            await TrySaveImagesAsync(turn).ConfigureAwait(false);
            if (WaitSeconds > 0 && Stream.IsPresent) {
                await Task.Delay(TimeSpan.FromSeconds(WaitSeconds), CancelToken).ConfigureAwait(false);
            }
            WriteObject(turn);
        } finally {
            subscription?.Dispose();
        }
    }

    private static async Task<bool> TryReadAccountAsync(IntelligenceXClient client) {
        try {
            await client.GetAccountAsync().ConfigureAwait(false);
            return true;
        } catch {
            return false;
        }
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

    private IntelligenceX.Json.JsonArray BuildInput() {
        var input = new IntelligenceX.Json.JsonArray();
        if (!string.IsNullOrWhiteSpace(Text)) {
            input.Add(new IntelligenceX.Json.JsonObject()
                .Add("type", "text")
                .Add("text", Text));
        }
        if (!string.IsNullOrWhiteSpace(ImagePath)) {
            input.Add(new IntelligenceX.Json.JsonObject()
                .Add("type", "image")
                .Add("path", ImagePath));
        }
        if (!string.IsNullOrWhiteSpace(ImageUrl)) {
            input.Add(new IntelligenceX.Json.JsonObject()
                .Add("type", "image")
                .Add("url", ImageUrl));
        }
        return input;
    }

    private static ChatInput BuildChatInput(IntelligenceX.Json.JsonArray input) {
        var chat = new ChatInput();
        foreach (var item in input) {
            var obj = item.AsObject();
            if (obj is not null) {
                chat.AddRaw(obj);
            }
        }
        return chat;
    }

    private IntelligenceX.Json.JsonArray BuildInputFromPipeline() {
        var input = new IntelligenceX.Json.JsonArray();
        foreach (var line in _pipelineInputs) {
            AppendDslLine(input, line);
        }
        if (input.Count == 0) {
            AppendText(input, string.Join(Environment.NewLine, _pipelineInputs));
        }
        return input;
    }

    private IntelligenceX.Json.JsonArray BuildInputFromDsl(string script) {
        var input = new IntelligenceX.Json.JsonArray();
        if (string.IsNullOrWhiteSpace(script)) {
            return input;
        }

        using var reader = new System.IO.StringReader(script);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            AppendDslLine(input, line);
        }

        if (input.Count == 0) {
            AppendText(input, script);
        }
        return input;
    }

    private static void AppendDslLine(IntelligenceX.Json.JsonArray input, string line) {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return;
        }

        if (TryParseDsl(trimmed, out var kind, out var value)) {
            switch (kind) {
                case "image":
                    input.Add(new IntelligenceX.Json.JsonObject()
                        .Add("type", "image")
                        .Add("path", value));
                    return;
                case "url":
                    input.Add(new IntelligenceX.Json.JsonObject()
                        .Add("type", "image")
                        .Add("url", value));
                    return;
                case "text":
                    AppendText(input, value);
                    return;
            }
        }

        AppendText(input, trimmed);
    }

    private static void AppendText(IntelligenceX.Json.JsonArray input, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }
        input.Add(new IntelligenceX.Json.JsonObject()
            .Add("type", "text")
            .Add("text", text));
    }

    private static bool TryParseDsl(string line, out string kind, out string value) {
        kind = string.Empty;
        value = string.Empty;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("@", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1);
        }

        var splitIndex = trimmed.IndexOfAny(new[] { ':', ' ' });
        if (splitIndex <= 0 || splitIndex >= trimmed.Length - 1) {
            return false;
        }

        var token = trimmed.Substring(0, splitIndex).Trim();
        var rest = trimmed.Substring(splitIndex + 1).TrimStart(' ', ':');
        if (string.IsNullOrWhiteSpace(rest)) {
            return false;
        }

        switch (token.ToLowerInvariant()) {
            case "image":
            case "img":
                kind = "image";
                value = rest;
                return true;
            case "url":
            case "image-url":
            case "imageurl":
                kind = "url";
                value = rest;
                return true;
            case "text":
                kind = "text";
                value = rest;
                return true;
            default:
                return false;
        }
    }

    private SandboxPolicy? BuildSandboxPolicy() {
        var workspace = Workspace;
        if (!IsNullOrWhiteSpace(workspace)) {
            return new SandboxPolicy("workspace", AllowNetwork.IsPresent, new[] { workspace! });
        }
        return null;
    }

    private async Task TrySaveImagesAsync(TurnInfo turn) {
        if (string.IsNullOrWhiteSpace(SaveImagesTo)) {
            return;
        }

        var images = turn.ImageOutputs;
        await TurnOutputSaver.SaveImagesAsync(images, SaveImagesTo!, turn.Id, DownloadImageUrls.IsPresent,
                OverwriteImages.IsPresent, ImageFileNamePrefix, Model, WriteWarning, WriteVerbose)
            .ConfigureAwait(false);
    }

    private static bool IsNullOrWhiteSpace(string? value) {
        if (value is null) {
            return true;
        }
        for (var i = 0; i < value.Length; i++) {
            if (!char.IsWhiteSpace(value[i])) {
                return false;
            }
        }
        return true;
    }
}
