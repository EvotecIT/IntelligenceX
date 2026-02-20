using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Provides typed construction of local runtime service launch arguments.
/// </summary>
internal static class ServiceLaunchArguments {
    internal sealed class ProfileOptions {
        public string? LoadProfileName { get; init; }
        public string? SaveProfileName { get; init; }
        public string? Model { get; init; }
        public string? OpenAITransport { get; init; }
        public string? OpenAIBaseUrl { get; init; }
        public string? OpenAIAuthMode { get; init; }
        public string? OpenAIApiKey { get; init; }
        public string? OpenAIBasicUsername { get; init; }
        public string? OpenAIBasicPassword { get; init; }
        public string? OpenAIAccountId { get; init; }
        public bool ClearOpenAIApiKey { get; init; }
        public bool ClearOpenAIBasicAuth { get; init; }
        public bool? OpenAIStreaming { get; init; }
        public bool? OpenAIAllowInsecureHttp { get; init; }
        public string? ReasoningEffort { get; init; }
        public string? ReasoningSummary { get; init; }
        public string? TextVerbosity { get; init; }
        public double? Temperature { get; init; }
        public bool? EnablePowerShellPack { get; init; }
        public bool? EnableTestimoXPack { get; init; }
        public bool? EnableOfficeImoPack { get; init; }
    }

    /// <summary>
    /// Builds runtime service arguments for the configured pipe and lifecycle mode.
    /// </summary>
    /// <param name="pipeName">Named-pipe identifier.</param>
    /// <param name="detachedServiceMode">Whether service is detached from app lifetime.</param>
    /// <param name="parentProcessId">Parent process id used for exit-on-disconnect mode.</param>
    /// <param name="profileOptions">Optional profile/runtime overrides passed to the service process.</param>
    /// <returns>Ordered argument vector.</returns>
    public static IReadOnlyList<string> Build(string pipeName, bool detachedServiceMode, int parentProcessId, ProfileOptions? profileOptions = null) {
        var normalizedPipe = (pipeName ?? string.Empty).Trim();
        if (normalizedPipe.Length == 0) {
            throw new ArgumentException("Pipe name cannot be empty.", nameof(pipeName));
        }

        var args = new List<string> {
            "--pipe",
            normalizedPipe
        };

        if (!detachedServiceMode) {
            args.Add("--exit-on-disconnect");
            args.Add("--parent-pid");
            args.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (profileOptions is null) {
            return args;
        }

        AddKeyValueArg(args, "--profile", profileOptions.LoadProfileName);
        AddKeyValueArg(args, "--save-profile", profileOptions.SaveProfileName);
        AddKeyValueArg(args, "--model", profileOptions.Model);

        var transport = NormalizeTransport(profileOptions.OpenAITransport);
        if (transport is not null) {
            args.Add("--openai-transport");
            args.Add(transport);
        }

        AddKeyValueArg(args, "--openai-base-url", profileOptions.OpenAIBaseUrl);
        AddKeyValueArg(args, "--openai-auth-mode", NormalizeCompatibleAuthMode(profileOptions.OpenAIAuthMode));
        if (profileOptions.ClearOpenAIApiKey) {
            args.Add("--openai-clear-api-key");
        } else {
            AddKeyValueArg(args, "--openai-api-key", profileOptions.OpenAIApiKey);
        }
        if (profileOptions.ClearOpenAIBasicAuth) {
            args.Add("--openai-clear-basic-auth");
        } else {
            AddKeyValueArg(args, "--openai-basic-username", profileOptions.OpenAIBasicUsername);
            AddKeyValueArg(args, "--openai-basic-password", profileOptions.OpenAIBasicPassword);
        }
        AddKeyValueArg(args, "--openai-account-id", profileOptions.OpenAIAccountId);

        if (profileOptions.OpenAIStreaming.HasValue) {
            args.Add(profileOptions.OpenAIStreaming.Value ? "--openai-stream" : "--openai-no-stream");
        }

        if (profileOptions.OpenAIAllowInsecureHttp == true) {
            args.Add("--openai-allow-insecure-http");
        }

        AddKeyValueArg(args, "--reasoning-effort", profileOptions.ReasoningEffort);
        AddKeyValueArg(args, "--reasoning-summary", profileOptions.ReasoningSummary);
        AddKeyValueArg(args, "--text-verbosity", profileOptions.TextVerbosity);
        if (profileOptions.Temperature.HasValue) {
            args.Add("--temperature");
            args.Add(profileOptions.Temperature.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (profileOptions.EnablePowerShellPack.HasValue) {
            args.Add(profileOptions.EnablePowerShellPack.Value ? "--enable-powershell-pack" : "--disable-powershell-pack");
        }

        if (profileOptions.EnableTestimoXPack.HasValue) {
            args.Add(profileOptions.EnableTestimoXPack.Value ? "--enable-testimox-pack" : "--disable-testimox-pack");
        }

        if (profileOptions.EnableOfficeImoPack.HasValue) {
            args.Add(profileOptions.EnableOfficeImoPack.Value ? "--enable-officeimo-pack" : "--disable-officeimo-pack");
        }

        return args;
    }

    private static void AddKeyValueArg(List<string> args, string key, string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        args.Add(key);
        args.Add(normalized);
    }

    private static string? NormalizeTransport(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return null;
        }

        return normalized switch {
            "native" => "native",
            "appserver" => "appserver",
            "compatible-http" => "compatible-http",
            "compatiblehttp" => "compatible-http",
            "copilot" => "copilot-cli",
            "copilot-cli" => "copilot-cli",
            "github-copilot" => "copilot-cli",
            "githubcopilot" => "copilot-cli",
            _ => throw new ArgumentException($"Unsupported OpenAI transport '{value}'.", nameof(value))
        };
    }

    private static string? NormalizeCompatibleAuthMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return null;
        }

        return normalized switch {
            "bearer" => "bearer",
            "api-key" => "bearer",
            "apikey" => "bearer",
            "token" => "bearer",
            "basic" => "basic",
            "none" => "none",
            "off" => "none",
            _ => throw new ArgumentException($"Unsupported compatible-http auth mode '{value}'.", nameof(value))
        };
    }
}
