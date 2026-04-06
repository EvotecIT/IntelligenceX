using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Provides typed construction of local runtime service launch arguments.
/// </summary>
internal static class ServiceLaunchArguments {
    internal sealed record PackToggle(string PackId, bool Enabled);

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
        public IReadOnlyList<PackToggle>? PackToggles { get; init; }
    }

    /// <summary>
    /// Builds runtime service arguments for the configured pipe and lifecycle mode.
    /// </summary>
    /// <param name="pipeName">Named-pipe identifier.</param>
    /// <param name="detachedServiceMode">Whether service is detached from app lifetime.</param>
    /// <param name="parentProcessId">Parent process id used for exit-on-disconnect mode.</param>
    /// <param name="profileOptions">Optional profile/runtime overrides passed to the service process.</param>
    /// <param name="additionalPluginPaths">Optional runtime-managed plugin roots passed as repeatable --plugin-path args.</param>
    /// <param name="additionalBuiltInToolProbePaths">Optional built-in tool assembly probe roots passed as repeatable --built-in-tool-probe-path args.</param>
    /// <param name="enableWorkspaceBuiltInToolOutputProbing">Whether workspace project output probing should be enabled for the launched service.</param>
    /// <returns>Ordered argument vector.</returns>
    public static IReadOnlyList<string> Build(
        string pipeName,
        bool detachedServiceMode,
        int parentProcessId,
        ProfileOptions? profileOptions = null,
        IReadOnlyList<string>? additionalPluginPaths = null,
        IReadOnlyList<string>? additionalBuiltInToolProbePaths = null,
        bool enableWorkspaceBuiltInToolOutputProbing = false) {
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

        if (profileOptions is not null) {
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

            AddPackToggleArgs(args, profileOptions.PackToggles);
        }

        AddMultiValueArg(args, "--plugin-path", additionalPluginPaths);
        AddMultiValueArg(args, "--built-in-tool-probe-path", additionalBuiltInToolProbePaths);
        if (enableWorkspaceBuiltInToolOutputProbing) {
            args.Add("--enable-workspace-built-in-tool-output-probing");
        }

        return args;
    }

    private static void AddMultiValueArg(List<string> args, string key, IReadOnlyList<string>? values) {
        if (values is null || values.Count == 0) {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var normalized = NormalizePathForArgument(values[i]);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            args.Add(key);
            args.Add(normalized);
        }
    }

    private static string NormalizePathForArgument(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        try {
            var fullPath = Path.GetFullPath(normalized);
            return TrimTrailingDirectorySeparators(fullPath);
        } catch {
            return normalized;
        }
    }

    private static string TrimTrailingDirectorySeparators(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        var root = Path.GetPathRoot(path);
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return root ?? path;
        }

        if (!string.IsNullOrWhiteSpace(root)) {
            var normalizedRoot = root!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmed, normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
                return root;
            }
        }

        return trimmed;
    }

    private static void AddPackToggleArgs(List<string> args, IReadOnlyList<PackToggle>? packToggles) {
        if (packToggles is null || packToggles.Count == 0) {
            return;
        }

        var effectiveToggles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < packToggles.Count; i++) {
            var toggle = packToggles[i];
            var normalizedPackId = NormalizePackId(toggle.PackId);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            effectiveToggles[normalizedPackId] = toggle.Enabled;
        }

        if (effectiveToggles.Count == 0) {
            return;
        }

        var ids = new List<string>(effectiveToggles.Keys);
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ids.Count; i++) {
            var normalizedPackId = ids[i];
            args.Add(effectiveToggles[normalizedPackId] ? "--enable-pack-id" : "--disable-pack-id");
            args.Add(normalizedPackId);
        }
    }

    private static string NormalizePackId(string? packId) {
        return ToolPackMetadataNormalizer.NormalizePackId(packId);
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
