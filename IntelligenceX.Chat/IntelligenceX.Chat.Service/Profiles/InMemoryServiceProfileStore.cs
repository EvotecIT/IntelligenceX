using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Service.Profiles;

internal sealed class InMemoryServiceProfileStore : IServiceProfileStore {
    private readonly Dictionary<string, ServiceProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public Task<ServiceProfile?> GetAsync(string name, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) {
            return Task.FromResult<ServiceProfile?>(null);
        }
        return Task.FromResult(_profiles.TryGetValue(name.Trim(), out var p) ? Clone(p) : null);
    }

    public Task UpsertAsync(string name, ServiceProfile profile, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        }
        _profiles[name.Trim()] = Clone(profile ?? throw new ArgumentNullException(nameof(profile)));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var names = new List<string>(_profiles.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private static ServiceProfile Clone(ServiceProfile profile) {
        // Keep cloning simple and explicit (no reflection); profiles are small.
        return new ServiceProfile {
            Model = profile.Model,
            OpenAITransport = profile.OpenAITransport,
            OpenAIBaseUrl = profile.OpenAIBaseUrl,
            OpenAIApiKey = profile.OpenAIApiKey,
            OpenAIStreaming = profile.OpenAIStreaming,
            OpenAIAllowInsecureHttp = profile.OpenAIAllowInsecureHttp,
            OpenAIAllowInsecureHttpNonLoopback = profile.OpenAIAllowInsecureHttpNonLoopback,
            ReasoningEffort = profile.ReasoningEffort,
            ReasoningSummary = profile.ReasoningSummary,
            TextVerbosity = profile.TextVerbosity,
            Temperature = profile.Temperature,
            MaxToolRounds = profile.MaxToolRounds,
            ParallelTools = profile.ParallelTools,
            TurnTimeoutSeconds = profile.TurnTimeoutSeconds,
            ToolTimeoutSeconds = profile.ToolTimeoutSeconds,
            AllowedRoots = new List<string>(profile.AllowedRoots ?? new List<string>()),
            AdDomainController = profile.AdDomainController,
            AdDefaultSearchBaseDn = profile.AdDefaultSearchBaseDn,
            AdMaxResults = profile.AdMaxResults,
            EnablePowerShellPack = profile.EnablePowerShellPack,
            PowerShellAllowWrite = profile.PowerShellAllowWrite,
            EnableTestimoXPack = profile.EnableTestimoXPack,
            EnableDefaultPluginPaths = profile.EnableDefaultPluginPaths,
            PluginPaths = new List<string>(profile.PluginPaths ?? new List<string>()),
            InstructionsFile = profile.InstructionsFile,
            MaxTableRows = profile.MaxTableRows,
            MaxSample = profile.MaxSample,
            Redact = profile.Redact
        };
    }
}
