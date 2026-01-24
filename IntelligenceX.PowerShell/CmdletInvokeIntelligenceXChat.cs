using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.AppServer.Models;

namespace IntelligenceX.PowerShell;

/// <summary>
/// <para type="synopsis">Super-easy chat command that handles connect, init, login, thread, and send.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "IntelligenceXChat", DefaultParameterSetName = "Text")]
[OutputType(typeof(TurnInfo))]
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
    /// <para type="description">Model identifier. Defaults to gpt-5.1-codex.</para>
    /// </summary>
    [Parameter]
    public string Model { get; set; } = "gpt-5.1-codex";

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

    protected override async Task ProcessRecordAsync() {
        if (ParameterSetName.Equals("Pipeline", StringComparison.OrdinalIgnoreCase)) {
            if (!string.IsNullOrWhiteSpace(InputObject)) {
                _pipelineInputs.Add(InputObject);
            }
            return;
        }

        var input = Dsl.IsPresent ? BuildInputFromDsl(Text) : BuildInput();
        await SendAsync(input).ConfigureAwait(false);
    }

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
            var options = new AppServerOptions();
            if (!string.IsNullOrWhiteSpace(ExecutablePath)) {
                options.ExecutablePath = ExecutablePath!;
            }
            if (!string.IsNullOrWhiteSpace(Arguments)) {
                options.Arguments = Arguments!;
            }
            if (!string.IsNullOrWhiteSpace(WorkingDirectory)) {
                options.WorkingDirectory = WorkingDirectory!;
            }
            client = await AppServerClient.StartAsync(options, CancelToken).ConfigureAwait(false);
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
                    await client.LoginWithApiKeyAsync(key, CancelToken).ConfigureAwait(false);
                } else {
                    var login = await client.StartChatGptLoginAsync(CancelToken).ConfigureAwait(false);
                    WriteVerbose($"Login URL: {login.AuthUrl}");
                    if (OpenBrowser.IsPresent) {
                        TryOpenUrl(login.AuthUrl);
                    }
                    await client.WaitForLoginCompletionAsync(login.LoginId, CancelToken).ConfigureAwait(false);
                }
            }
        }

        if (NewThread.IsPresent || string.IsNullOrWhiteSpace(ClientContext.DefaultThreadId)) {
            var thread = await client.StartThreadAsync(Model, cancellationToken: CancelToken).ConfigureAwait(false);
            ClientContext.DefaultThreadId = thread.Id;
        }

        var sandboxPolicy = BuildSandboxPolicy();
        var cwd = Workspace ?? WorkingDirectory;
        var approval = ApprovalPolicy;
        if (string.IsNullOrWhiteSpace(approval) && !string.IsNullOrWhiteSpace(Workspace)) {
            approval = "auto";
        }

        if (Stream.IsPresent) {
            void Handler(object? sender, IntelligenceX.Rpc.JsonRpcNotificationEventArgs args) {
                var text = args.Params?.AsObject()?.GetObject("delta")?.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    WriteObject(text);
                }
            }

            client.NotificationReceived += Handler;
            try {
                var turn = await client.StartTurnAsync(ClientContext.DefaultThreadId!, input, Model, cwd, approval, sandboxPolicy, CancelToken)
                    .ConfigureAwait(false);
                await TrySaveImagesAsync(turn).ConfigureAwait(false);
                if (WaitSeconds > 0) {
                    await Task.Delay(TimeSpan.FromSeconds(WaitSeconds), CancelToken).ConfigureAwait(false);
                }
                WriteObject(turn);
            } finally {
                client.NotificationReceived -= Handler;
            }
            return;
        }

        var result = await client.StartTurnAsync(ClientContext.DefaultThreadId!, input, Model, cwd, approval, sandboxPolicy, CancelToken)
            .ConfigureAwait(false);
        await TrySaveImagesAsync(result).ConfigureAwait(false);
        WriteObject(result);
    }

    private static async Task<bool> TryReadAccountAsync(AppServerClient client) {
        try {
            await client.ReadAccountAsync().ConfigureAwait(false);
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
        if (!string.IsNullOrWhiteSpace(workspace)) {
            return new SandboxPolicy("workspace", AllowNetwork.IsPresent, new[] { workspace });
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
}
