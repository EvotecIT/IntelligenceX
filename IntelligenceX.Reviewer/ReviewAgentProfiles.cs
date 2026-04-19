using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal bool TryGetAgentProfile(string? id, out ReviewAgentProfileSettings profile) {
        profile = new ReviewAgentProfileSettings();
        if (string.IsNullOrWhiteSpace(id) || AgentProfiles.Count == 0) {
            return false;
        }
        return AgentProfiles.TryGetValue(id.Trim(), out profile!);
    }

    internal void ApplyAgentProfile(string? id) {
        if (!TryGetAgentProfile(id, out var profile)) {
            return;
        }

        ApplyAgentProfile(profile);
    }

    internal void ApplyAgentProfile(ReviewAgentProfileSettings profile) {
        AgentProfile = profile.Id;
        var provider = profile.ResolveProvider();
        if (provider.HasValue) {
            Provider = provider.Value;
        }
        if (!string.IsNullOrWhiteSpace(profile.Model)) {
            Model = profile.Model.Trim();
        }
        if (profile.ReasoningEffort.HasValue) {
            ReasoningEffort = profile.ReasoningEffort;
        }
        if (profile.OpenAITransport.HasValue) {
            OpenAITransport = profile.OpenAITransport.Value;
        }
        OpenAiAccountId = UseIfSet(profile.OpenAiAccountId, OpenAiAccountId);

        if (profile.CopilotTransport.HasValue) {
            CopilotTransport = profile.CopilotTransport.Value;
        }
        CopilotModel = UseIfSet(profile.CopilotModel, CopilotModel);
        if (string.IsNullOrWhiteSpace(profile.CopilotModel) &&
            provider == ReviewProvider.Copilot &&
            !string.IsNullOrWhiteSpace(profile.Model)) {
            CopilotModel = profile.Model.Trim();
        }
        if (!string.IsNullOrWhiteSpace(profile.CopilotLauncher)) {
            CopilotLauncher = NormalizeCopilotLauncher(profile.CopilotLauncher, CopilotLauncher);
        }
        CopilotCliPath = UseIfSet(profile.CopilotCliPath, CopilotCliPath);
        CopilotCliUrl = UseIfSet(profile.CopilotCliUrl, CopilotCliUrl);
        CopilotWorkingDirectory = UseIfSet(profile.CopilotWorkingDirectory, CopilotWorkingDirectory);
        if (profile.CopilotAutoInstall.HasValue) {
            CopilotAutoInstall = profile.CopilotAutoInstall.Value;
        }
        CopilotAutoInstallMethod = UseIfSet(profile.CopilotAutoInstallMethod, CopilotAutoInstallMethod);
        if (profile.CopilotAutoInstallPrerelease.HasValue) {
            CopilotAutoInstallPrerelease = profile.CopilotAutoInstallPrerelease.Value;
        }
        if (profile.CopilotInheritEnvironment.HasValue) {
            CopilotInheritEnvironment = profile.CopilotInheritEnvironment.Value;
        }
        if (profile.CopilotEnvAllowlist is { Count: > 0 }) {
            CopilotEnvAllowlist = profile.CopilotEnvAllowlist;
        }
        if (profile.CopilotEnv is { Count: > 0 }) {
            CopilotEnv = MergeStringMap(CopilotEnv, profile.CopilotEnv);
        }
        CopilotDirectUrl = UseIfSet(profile.CopilotDirectUrl, CopilotDirectUrl);
        CopilotDirectTokenEnv = UseIfSet(profile.CopilotDirectTokenEnv, CopilotDirectTokenEnv);
        if (profile.CopilotDirectTimeoutSeconds.HasValue && profile.CopilotDirectTimeoutSeconds.Value > 0) {
            CopilotDirectTimeoutSeconds = profile.CopilotDirectTimeoutSeconds.Value;
        }
        if (profile.CopilotDirectHeaders is { Count: > 0 }) {
            CopilotDirectHeaders = MergeStringMap(CopilotDirectHeaders, profile.CopilotDirectHeaders);
        }

        OpenAICompatibleBaseUrl = UseIfSet(profile.OpenAICompatibleBaseUrl, OpenAICompatibleBaseUrl);
        OpenAICompatibleApiKeyEnv = UseIfSet(profile.OpenAICompatibleApiKeyEnv, OpenAICompatibleApiKeyEnv);
        if (profile.OpenAICompatibleTimeoutSeconds.HasValue && profile.OpenAICompatibleTimeoutSeconds.Value > 0) {
            OpenAICompatibleTimeoutSeconds = profile.OpenAICompatibleTimeoutSeconds.Value;
        }

        AnthropicApiKeyEnv = UseIfSet(profile.AnthropicApiKeyEnv, AnthropicApiKeyEnv);
        AnthropicBaseUrl = UseIfSet(profile.AnthropicBaseUrl, AnthropicBaseUrl)!;
        if (profile.AnthropicTimeoutSeconds.HasValue && profile.AnthropicTimeoutSeconds.Value > 0) {
            AnthropicTimeoutSeconds = profile.AnthropicTimeoutSeconds.Value;
        }
    }

    private static string? UseIfSet(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static IReadOnlyDictionary<string, string> MergeStringMap(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second) {
        var result = new Dictionary<string, string>(first, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in second) {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null) {
                continue;
            }
            result[entry.Key] = entry.Value;
        }
        return result;
    }
}
