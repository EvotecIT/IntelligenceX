using System;
using System.IO;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Configuration;

public sealed class IntelligenceXConfig {
    public OpenAIConfig OpenAI { get; } = new();
    public CopilotConfig Copilot { get; } = new();

    public static bool TryLoad(out IntelligenceXConfig config, string? path = null, string? baseDirectory = null) {
        config = new IntelligenceXConfig();
        var resolved = ResolvePath(path, baseDirectory);
        if (resolved is null || !File.Exists(resolved)) {
            return false;
        }

        var json = File.ReadAllText(resolved);
        var root = JsonLite.Parse(json).AsObject();
        if (root is null) {
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

    public static IntelligenceXConfig Load(string? path = null, string? baseDirectory = null) {
        if (!TryLoad(out var config, path, baseDirectory)) {
            throw new FileNotFoundException("IntelligenceX config not found.");
        }
        return config;
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

public sealed class OpenAIConfig {
    public string? DefaultModel { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? ApprovalPolicy { get; set; }
    public bool? AllowNetwork { get; set; }
    public string? Transport { get; set; }
    public string? Originator { get; set; }
    public string? ResponsesUrl { get; set; }
    public string? ChatGptApiBaseUrl { get; set; }
    public string? Instructions { get; set; }
    public string? TextVerbosity { get; set; }
    public string? AppServerPath { get; set; }
    public string? AppServerArgs { get; set; }
    public string? AppServerWorkingDirectory { get; set; }
    public bool? OpenBrowser { get; set; }
    public bool? PrintLoginUrl { get; set; }
    public bool? AutoLogin { get; set; }
    public string? LoginMode { get; set; }
    public long? MaxImageBytes { get; set; }
    public bool? RequireWorkspaceForFileAccess { get; set; }
    public string? ReasoningEffort { get; set; }
    public string? ReasoningSummary { get; set; }

    internal void ReadFrom(JsonObject obj) {
        DefaultModel = obj.GetString("defaultModel") ?? DefaultModel;
        WorkingDirectory = obj.GetString("workingDirectory") ?? WorkingDirectory;
        ApprovalPolicy = obj.GetString("approvalPolicy") ?? ApprovalPolicy;
        Transport = obj.GetString("transport") ?? Transport;
        Originator = obj.GetString("originator") ?? Originator;
        ResponsesUrl = obj.GetString("responsesUrl") ?? ResponsesUrl;
        ChatGptApiBaseUrl = obj.GetString("chatGptApiBaseUrl") ?? obj.GetString("chatgptApiBaseUrl") ?? ChatGptApiBaseUrl;
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

public sealed class CopilotConfig {
    public string? CliPath { get; set; }
    public string? CliUrl { get; set; }
    public bool? AutoInstall { get; set; }

    internal void ReadFrom(JsonObject obj) {
        CliPath = obj.GetString("cliPath") ?? CliPath;
        CliUrl = obj.GetString("cliUrl") ?? CliUrl;
        AutoInstall = ReadBool(obj, "autoInstall", AutoInstall);
    }

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


