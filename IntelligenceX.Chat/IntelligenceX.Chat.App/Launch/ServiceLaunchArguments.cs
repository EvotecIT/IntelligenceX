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
        public string? OpenAIApiKey { get; init; }
        public bool ClearOpenAIApiKey { get; init; }
        public bool? OpenAIStreaming { get; init; }
        public bool? OpenAIAllowInsecureHttp { get; init; }
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
        if (profileOptions.ClearOpenAIApiKey) {
            args.Add("--openai-clear-api-key");
        } else {
            AddKeyValueArg(args, "--openai-api-key", profileOptions.OpenAIApiKey);
        }

        if (profileOptions.OpenAIStreaming.HasValue) {
            args.Add(profileOptions.OpenAIStreaming.Value ? "--openai-stream" : "--openai-no-stream");
        }

        if (profileOptions.OpenAIAllowInsecureHttp == true) {
            args.Add("--openai-allow-insecure-http");
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
            _ => throw new ArgumentException($"Unsupported OpenAI transport '{value}'.", nameof(value))
        };
    }
}
