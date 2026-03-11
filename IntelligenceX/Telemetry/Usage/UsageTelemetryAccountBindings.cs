using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Thread-safe in-memory store for manual usage-account bindings.
/// </summary>
public sealed class InMemoryUsageAccountBindingStore : IUsageAccountBindingStore {
    private readonly ConcurrentDictionary<string, UsageAccountBindingRecord> _bindings =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(UsageAccountBindingRecord binding) {
        if (binding is null) {
            throw new ArgumentNullException(nameof(binding));
        }

        _bindings[binding.Id] = binding;
    }

    /// <inheritdoc />
    public bool TryGet(string id, out UsageAccountBindingRecord binding) {
        if (string.IsNullOrWhiteSpace(id)) {
            binding = null!;
            return false;
        }

        return _bindings.TryGetValue(id.Trim(), out binding!);
    }

    /// <inheritdoc />
    public IReadOnlyList<UsageAccountBindingRecord> GetAll() {
        return _bindings.Values
            .OrderBy(value => value.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Resolves account identity from manual account-binding rules.
/// </summary>
public sealed class UsageAccountBindingResolver : IUsageAccountResolver {
    private readonly IUsageAccountBindingStore _bindingStore;

    /// <summary>
    /// Initializes a new binding-backed account resolver.
    /// </summary>
    public UsageAccountBindingResolver(IUsageAccountBindingStore bindingStore) {
        _bindingStore = bindingStore ?? throw new ArgumentNullException(nameof(bindingStore));
    }

    /// <inheritdoc />
    public ResolvedUsageAccount Resolve(UsageEventRecord usageEvent, RawArtifactDescriptor? artifact = null) {
        if (usageEvent is null) {
            throw new ArgumentNullException(nameof(usageEvent));
        }

        var match = _bindingStore.GetAll()
            .Where(binding => binding.Enabled)
            .Where(binding => string.Equals(binding.ProviderId, usageEvent.ProviderId, StringComparison.OrdinalIgnoreCase))
            .Select(binding => new {
                Binding = binding,
                Score = GetMatchScore(binding, usageEvent)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Binding.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Binding)
            .FirstOrDefault();

        if (match is null) {
            return new ResolvedUsageAccount {
                ProviderAccountId = usageEvent.ProviderAccountId,
                AccountLabel = usageEvent.AccountLabel,
                PersonLabel = usageEvent.PersonLabel
            };
        }

        return new ResolvedUsageAccount {
            ProviderAccountId = NormalizeOptional(match.ProviderAccountId) ?? usageEvent.ProviderAccountId,
            AccountLabel = NormalizeOptional(match.AccountLabel) ?? usageEvent.AccountLabel,
            PersonLabel = NormalizeOptional(match.PersonLabel) ?? usageEvent.PersonLabel
        };
    }

    private static int GetMatchScore(UsageAccountBindingRecord binding, UsageEventRecord usageEvent) {
        var score = 0;
        var hasMatcher = false;

        var sourceRootId = NormalizeOptional(binding.SourceRootId);
        if (sourceRootId is not null) {
            hasMatcher = true;
            if (!string.Equals(sourceRootId, NormalizeOptional(usageEvent.SourceRootId), StringComparison.OrdinalIgnoreCase)) {
                return 0;
            }
            score += 100;
        }

        var matchProviderAccountId = NormalizeOptional(binding.MatchProviderAccountId);
        if (matchProviderAccountId is not null) {
            hasMatcher = true;
            if (!string.Equals(matchProviderAccountId, NormalizeOptional(usageEvent.ProviderAccountId), StringComparison.OrdinalIgnoreCase)) {
                return 0;
            }
            score += 50;
        }

        var matchAccountLabel = NormalizeOptional(binding.MatchAccountLabel);
        if (matchAccountLabel is not null) {
            hasMatcher = true;
            if (!string.Equals(matchAccountLabel, NormalizeOptional(usageEvent.AccountLabel), StringComparison.OrdinalIgnoreCase)) {
                return 0;
            }
            score += 25;
        }

        return hasMatcher ? score : 0;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
