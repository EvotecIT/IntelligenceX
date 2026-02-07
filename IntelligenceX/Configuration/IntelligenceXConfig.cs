using System;
using System.IO;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Configuration;

/// <summary>
/// Loads and holds the IntelligenceX configuration from disk.
/// </summary>
public sealed class IntelligenceXConfig {
    /// <summary>
    /// OpenAI provider configuration.
    /// </summary>
    public OpenAIConfig OpenAI { get; } = new();
    /// <summary>
    /// Copilot provider configuration.
    /// </summary>
    public CopilotConfig Copilot { get; } = new();

    /// <summary>
    /// Attempts to load configuration from the provided path, the environment, or the default location.
    /// </summary>
    /// <param name="config">The populated configuration on success.</param>
    /// <param name="path">Optional explicit config path.</param>
    /// <param name="baseDirectory">Optional base directory for resolving the default config path.</param>
    /// <returns><c>true</c> when the configuration was loaded; otherwise <c>false</c>.</returns>
    public static bool TryLoad(out IntelligenceXConfig config, string? path = null, string? baseDirectory = null) {
        return TryLoadInternal(out config, path, baseDirectory, out _);
    }

    /// <summary>
    /// Loads configuration or throws if it cannot be found or parsed.
    /// </summary>
    /// <param name="path">Optional explicit config path.</param>
    /// <param name="baseDirectory">Optional base directory for resolving the default config path.</param>
    /// <returns>The loaded configuration.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the configuration file cannot be found.</exception>
    /// <exception cref="InvalidDataException">Thrown when the configuration file contains invalid JSON.</exception>
    public static IntelligenceXConfig Load(string? path = null, string? baseDirectory = null) {
        if (!TryLoadInternal(out var config, path, baseDirectory, out var failure)) {
            if (failure == LoadFailure.InvalidJson) {
                throw new InvalidDataException("IntelligenceX config contains invalid JSON.");
            }
            throw new FileNotFoundException("IntelligenceX config not found.");
        }
        return config;
    }

    private enum LoadFailure {
        None,
        NotFound,
        InvalidJson
    }

    private static bool TryLoadInternal(out IntelligenceXConfig config, string? path, string? baseDirectory, out LoadFailure failure) {
        config = new IntelligenceXConfig();
        failure = LoadFailure.None;
        var resolved = ResolvePath(path, baseDirectory);
        if (resolved is null || !File.Exists(resolved)) {
            failure = LoadFailure.NotFound;
            return false;
        }

        var json = File.ReadAllText(resolved);
        JsonValue? rootValue;
        try {
            rootValue = JsonLite.Parse(json);
        } catch (FormatException) {
            failure = LoadFailure.InvalidJson;
            return false;
        }
        var root = rootValue.AsObject();
        if (root is null) {
            failure = LoadFailure.InvalidJson;
            return false;
        }

        var openai = root.GetObject("openai");
        if (openai is not null) {
            config.OpenAI.ReadFrom(openai);
        }

        var copilot = root.GetObject("copilot");
        if (copilot is not null) {
            config.Copilot.ReadFrom(copilot);
        }

        return true;
    }

    private static string? ResolvePath(string? path, string? baseDirectory) {
        if (!string.IsNullOrWhiteSpace(path)) {
            return path;
        }
        var env = Environment.GetEnvironmentVariable("INTELLIGENCEX_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(env)) {
            return env;
        }
        var root = baseDirectory ?? Environment.CurrentDirectory;
        return Path.Combine(root, ".intelligencex", "config.json");
    }
}

/// <summary>
/// OpenAI-specific configuration that can be applied to client and session options.
/// </summary>
public sealed class OpenAIConfig {
    /// <summary>
    /// Default model name to use for new sessions.
    /// </summary>
    public string? DefaultModel { get; set; }
    /// <summary>
    /// Default working directory for file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Default approval policy string (for example, "never" or "auto").
    /// </summary>
    public string? ApprovalPolicy { get; set; }
    /// <summary>
    /// Enables or disables network access for tools that can use it.
    /// </summary>
    public bool? AllowNetwork { get; set; }
    /// <summary>
    /// Transport identifier (for example, "native" or "appserver").
    /// </summary>
    public string? Transport { get; set; }
    /// <summary>
    /// Originator value reported for native OpenAI requests.
    /// </summary>
    public string? Originator { get; set; }
    /// <summary>
    /// Overrides the OpenAI responses endpoint URL.
    /// </summary>
    public string? ResponsesUrl { get; set; }
    /// <summary>
    /// Overrides the ChatGPT API base URL for native transport.
    /// </summary>
    public string? ChatGptApiBaseUrl { get; set; }
    /// <summary>
    /// Optional ChatGPT account id to use when multiple bundles exist in the auth store.
    /// </summary>
    public string? AuthAccountId { get; set; }
    /// <summary>
    /// Default system instructions for native sessions.
    /// </summary>
    public string? Instructions { get; set; }
    /// <summary>
    /// Text verbosity hint for native requests.
    /// </summary>
    public string? TextVerbosity { get; set; }
    /// <summary>
    /// Path to the OpenAI app-server executable.
    /// </summary>
    public string? AppServerPath { get; set; }
    /// <summary>
    /// Arguments passed to the app-server executable.
    /// </summary>
    public string? AppServerArgs { get; set; }
    /// <summary>
    /// Working directory for the app-server process.
    /// </summary>
    public string? AppServerWorkingDirectory { get; set; }
    /// <summary>
    /// Opens a browser during ChatGPT login flows when enabled.
    /// </summary>
    public bool? OpenBrowser { get; set; }
    /// <summary>
    /// Prints the login URL to the console during ChatGPT login flows when enabled.
    /// </summary>
    public bool? PrintLoginUrl { get; set; }
    /// <summary>
    /// Attempts automatic login when enabled.
    /// </summary>
    public bool? AutoLogin { get; set; }
    /// <summary>
    /// Login mode override ("chatgpt", "api", or "none").
    /// </summary>
    public string? LoginMode { get; set; }
    /// <summary>
    /// Maximum allowed image payload size in bytes.
    /// </summary>
    public long? MaxImageBytes { get; set; }
    /// <summary>
    /// Requires the workspace path to be set before file access is allowed.
    /// </summary>
    public bool? RequireWorkspaceForFileAccess { get; set; }
    /// <summary>
    /// Reasoning effort hint ("low", "medium", "high").
    /// </summary>
    public string? ReasoningEffort { get; set; }
    /// <summary>
    /// Reasoning summary hint ("auto", "concise", "detailed").
    /// </summary>
    public string? ReasoningSummary { get; set; }

    internal void ReadFrom(JsonObject obj) {
        DefaultModel = obj.GetString("defaultModel") ?? DefaultModel;
        WorkingDirectory = obj.GetString("workingDirectory") ?? WorkingDirectory;
        ApprovalPolicy = obj.GetString("approvalPolicy") ?? ApprovalPolicy;
        Transport = obj.GetString("transport") ?? Transport;
        Originator = obj.GetString("originator") ?? Originator;
        ResponsesUrl = obj.GetString("responsesUrl") ?? ResponsesUrl;
        ChatGptApiBaseUrl = obj.GetString("chatGptApiBaseUrl") ?? obj.GetString("chatgptApiBaseUrl") ?? ChatGptApiBaseUrl;
        AuthAccountId = obj.GetString("authAccountId") ?? obj.GetString("openaiAccountId") ?? obj.GetString("openAiAccountId") ?? AuthAccountId;
        Instructions = obj.GetString("instructions") ?? Instructions;
        TextVerbosity = obj.GetString("textVerbosity") ?? TextVerbosity;
        AppServerPath = obj.GetString("appServerPath") ?? AppServerPath;
        AppServerArgs = obj.GetString("appServerArgs") ?? AppServerArgs;
        AppServerWorkingDirectory = obj.GetString("appServerWorkingDirectory") ?? AppServerWorkingDirectory;
        OpenBrowser = ReadBool(obj, "openBrowser", OpenBrowser);
        PrintLoginUrl = ReadBool(obj, "printLoginUrl", PrintLoginUrl);
        AutoLogin = ReadBool(obj, "autoLogin", AutoLogin);
        AllowNetwork = ReadBool(obj, "allowNetwork", AllowNetwork);
        LoginMode = obj.GetString("loginMode") ?? LoginMode;
        MaxImageBytes = ReadLong(obj, "maxImageBytes", MaxImageBytes);
        RequireWorkspaceForFileAccess = ReadBool(obj, "requireWorkspaceForFileAccess", RequireWorkspaceForFileAccess);
        ReasoningEffort = obj.GetString("reasoningEffort") ?? ReasoningEffort;
        ReasoningSummary = obj.GetString("reasoningSummary") ?? ReasoningSummary;
    }

    /// <summary>
    /// Applies settings to an <see cref="EasySessionOptions"/> instance.
    /// </summary>
    /// <param name="options">Session options to populate.</param>
    public void ApplyTo(EasySessionOptions options) {
        if (!string.IsNullOrWhiteSpace(DefaultModel)) {
            options.DefaultModel = DefaultModel!;
        }
        if (!string.IsNullOrWhiteSpace(WorkingDirectory)) {
            options.WorkingDirectory = WorkingDirectory;
        }
        if (!string.IsNullOrWhiteSpace(ApprovalPolicy)) {
            options.ApprovalPolicy = ApprovalPolicy;
        }
        if (AllowNetwork.HasValue) {
            options.AllowNetwork = AllowNetwork.Value;
        }
        if (!string.IsNullOrWhiteSpace(Transport)) {
            options.TransportKind = ParseTransport(Transport!);
        }
        if (!string.IsNullOrWhiteSpace(Originator)) {
            options.NativeOptions.Originator = Originator!;
        }
        if (!string.IsNullOrWhiteSpace(ResponsesUrl)) {
            options.NativeOptions.ResponsesUrl = ResponsesUrl!;
        }
        if (!string.IsNullOrWhiteSpace(ChatGptApiBaseUrl)) {
            options.NativeOptions.ChatGptApiBaseUrl = ChatGptApiBaseUrl!;
        }
        if (!string.IsNullOrWhiteSpace(AuthAccountId)) {
            options.NativeOptions.AuthAccountId = AuthAccountId!;
        }
        if (!string.IsNullOrWhiteSpace(Instructions)) {
            options.NativeOptions.Instructions = Instructions!;
        }
        if (!string.IsNullOrWhiteSpace(TextVerbosity)) {
            var verbosity = ChatEnumParser.ParseTextVerbosity(TextVerbosity);
            if (verbosity.HasValue) {
                options.NativeOptions.TextVerbosity = verbosity.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(ReasoningEffort)) {
            var effort = ChatEnumParser.ParseReasoningEffort(ReasoningEffort);
            if (effort.HasValue) {
                options.NativeOptions.ReasoningEffort = effort.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(ReasoningSummary)) {
            var summary = ChatEnumParser.ParseReasoningSummary(ReasoningSummary);
            if (summary.HasValue) {
                options.NativeOptions.ReasoningSummary = summary.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(AppServerPath)) {
            options.AppServerOptions.ExecutablePath = AppServerPath!;
        }
        if (!string.IsNullOrWhiteSpace(AppServerArgs)) {
            options.AppServerOptions.Arguments = AppServerArgs!;
        }
        if (!string.IsNullOrWhiteSpace(AppServerWorkingDirectory)) {
            options.AppServerOptions.WorkingDirectory = AppServerWorkingDirectory;
        }
        if (OpenBrowser.HasValue) {
            options.OpenBrowser = OpenBrowser.Value;
        }
        if (PrintLoginUrl.HasValue) {
            options.PrintLoginUrl = PrintLoginUrl.Value;
        }
        if (AutoLogin.HasValue) {
            options.AutoLogin = AutoLogin.Value;
        }
        if (MaxImageBytes.HasValue) {
            options.MaxImageBytes = MaxImageBytes.Value;
        }
        if (RequireWorkspaceForFileAccess.HasValue) {
            options.RequireWorkspaceForFileAccess = RequireWorkspaceForFileAccess.Value;
        }
        if (!string.IsNullOrWhiteSpace(LoginMode)) {
            options.Login = ParseLoginMode(LoginMode!);
        }
    }

    /// <summary>
    /// Applies settings to an <see cref="IntelligenceXClientOptions"/> instance.
    /// </summary>
    /// <param name="options">Client options to populate.</param>
    public void ApplyTo(IntelligenceXClientOptions options) {
        if (!string.IsNullOrWhiteSpace(DefaultModel)) {
            options.DefaultModel = DefaultModel!;
        }
        if (!string.IsNullOrWhiteSpace(WorkingDirectory)) {
            options.DefaultWorkingDirectory = WorkingDirectory;
        }
        if (!string.IsNullOrWhiteSpace(ApprovalPolicy)) {
            options.DefaultApprovalPolicy = ApprovalPolicy;
        }
        if (!string.IsNullOrWhiteSpace(Transport)) {
            options.TransportKind = ParseTransport(Transport!);
        }
        if (!string.IsNullOrWhiteSpace(Originator)) {
            options.NativeOptions.Originator = Originator!;
        }
        if (!string.IsNullOrWhiteSpace(ResponsesUrl)) {
            options.NativeOptions.ResponsesUrl = ResponsesUrl!;
        }
        if (!string.IsNullOrWhiteSpace(ChatGptApiBaseUrl)) {
            options.NativeOptions.ChatGptApiBaseUrl = ChatGptApiBaseUrl!;
        }
        if (!string.IsNullOrWhiteSpace(AuthAccountId)) {
            options.NativeOptions.AuthAccountId = AuthAccountId!;
        }
        if (!string.IsNullOrWhiteSpace(Instructions)) {
            options.NativeOptions.Instructions = Instructions!;
        }
        if (!string.IsNullOrWhiteSpace(TextVerbosity)) {
            var verbosity = ChatEnumParser.ParseTextVerbosity(TextVerbosity);
            if (verbosity.HasValue) {
                options.NativeOptions.TextVerbosity = verbosity.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(ReasoningEffort)) {
            var effort = ChatEnumParser.ParseReasoningEffort(ReasoningEffort);
            if (effort.HasValue) {
                options.NativeOptions.ReasoningEffort = effort.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(ReasoningSummary)) {
            var summary = ChatEnumParser.ParseReasoningSummary(ReasoningSummary);
            if (summary.HasValue) {
                options.NativeOptions.ReasoningSummary = summary.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(AppServerPath)) {
            options.AppServerOptions.ExecutablePath = AppServerPath!;
        }
        if (!string.IsNullOrWhiteSpace(AppServerArgs)) {
            options.AppServerOptions.Arguments = AppServerArgs!;
        }
        if (!string.IsNullOrWhiteSpace(AppServerWorkingDirectory)) {
            options.AppServerOptions.WorkingDirectory = AppServerWorkingDirectory;
        }
    }

    /// <summary>
    /// Applies app-server related settings to an <see cref="AppServerOptions"/> instance.
    /// </summary>
    /// <param name="options">App server options to populate.</param>
    public void ApplyTo(AppServerOptions options) {
        if (!string.IsNullOrWhiteSpace(AppServerPath)) {
            options.ExecutablePath = AppServerPath!;
        }
        if (!string.IsNullOrWhiteSpace(AppServerArgs)) {
            options.Arguments = AppServerArgs!;
        }
        if (!string.IsNullOrWhiteSpace(AppServerWorkingDirectory)) {
            options.WorkingDirectory = AppServerWorkingDirectory;
        }
    }

    private static EasyLoginMode ParseLoginMode(string value) {
        return value.Trim().ToLowerInvariant() switch {
            "api" or "apikey" => EasyLoginMode.ApiKey,
            "none" => EasyLoginMode.None,
            _ => EasyLoginMode.ChatGpt
        };
    }

    private static OpenAITransportKind ParseTransport(string value) {
        return value.Trim().ToLowerInvariant() switch {
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => OpenAITransportKind.Native
        };
    }

    private static bool? ReadBool(JsonObject obj, string key, bool? current) {
        if (!obj.TryGetValue(key, out var value) || value is null) {
            return current;
        }
        return value.AsBoolean();
    }

    private static long? ReadLong(JsonObject obj, string key, long? current) {
        if (!obj.TryGetValue(key, out var value) || value is null) {
            return current;
        }
        return value.AsInt64();
    }
}

/// <summary>
/// GitHub Copilot-specific configuration.
/// </summary>
public sealed class CopilotConfig {
    /// <summary>
    /// Path to the Copilot CLI executable.
    /// </summary>
    public string? CliPath { get; set; }
    /// <summary>
    /// Download URL for the Copilot CLI.
    /// </summary>
    public string? CliUrl { get; set; }
    /// <summary>
    /// Automatically installs the Copilot CLI when missing.
    /// </summary>
    public bool? AutoInstall { get; set; }

    internal void ReadFrom(JsonObject obj) {
        CliPath = obj.GetString("cliPath") ?? CliPath;
        CliUrl = obj.GetString("cliUrl") ?? CliUrl;
        AutoInstall = ReadBool(obj, "autoInstall", AutoInstall);
    }

    /// <summary>
    /// Applies settings to a <see cref="CopilotClientOptions"/> instance.
    /// </summary>
    /// <param name="options">Copilot client options to populate.</param>
    public void ApplyTo(CopilotClientOptions options) {
        if (!string.IsNullOrWhiteSpace(CliPath)) {
            options.CliPath = CliPath;
        }
        if (!string.IsNullOrWhiteSpace(CliUrl)) {
            options.CliUrl = CliUrl;
        }
        if (AutoInstall.HasValue) {
            options.AutoInstallCli = AutoInstall.Value;
        }
    }

    private static bool? ReadBool(JsonObject obj, string key, bool? current) {
        if (!obj.TryGetValue(key, out var value) || value is null) {
            return current;
        }
        return value.AsBoolean();
    }
}

