using System;
using System.Collections.Generic;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Defines a tool that can be invoked by the model.
/// </summary>
public sealed class ToolDefinition {
    private static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly AsyncLocal<Action<string>?> MalformedTaxonomyTagDroppedObserver = new();

    /// <summary>
    /// Initializes a new tool definition.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Tool description.</param>
    /// <param name="parameters">JSON schema for tool parameters.</param>
    /// <param name="displayName">Optional human-friendly tool display name.</param>
    /// <param name="category">Optional tool category label.</param>
    /// <param name="tags">Optional tags used for model/tooling guidance.</param>
    /// <param name="writeGovernance">Optional write-governance contract for mutating tools.</param>
    /// <param name="aliases">Optional aliases that should invoke the same tool implementation.</param>
    /// <param name="aliasOf">Optional canonical tool name when this definition is an alias.</param>
    /// <param name="authentication">Optional authentication contract for tools that require/declare auth behavior.</param>
    /// <param name="routing">Optional routing contract for host-side orchestration.</param>
    /// <param name="setup">Optional setup contract for prerequisites and setup hints.</param>
    /// <param name="handoff">Optional handoff contract for cross-pack argument mappings.</param>
    /// <param name="recovery">Optional recovery contract for tool-owned resilience behavior.</param>
    public ToolDefinition(
        string name,
        string? description = null,
        JsonObject? parameters = null,
        string? displayName = null,
        string? category = null,
        IReadOnlyList<string>? tags = null,
        ToolWriteGovernanceContract? writeGovernance = null,
        IReadOnlyList<ToolAliasDefinition>? aliases = null,
        string? aliasOf = null,
        ToolAuthenticationContract? authentication = null,
        ToolRoutingContract? routing = null,
        ToolSetupContract? setup = null,
        ToolHandoffContract? handoff = null,
        ToolRecoveryContract? recovery = null) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Tool name cannot be empty.", nameof(name));
        }
        Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(aliasOf) &&
            string.Equals(Name, aliasOf!.Trim(), StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Alias cannot reference itself.", nameof(aliasOf));
        }

        Description = description;
        Parameters = parameters;
        DisplayName = NormalizeOptionalText(displayName);
        Category = NormalizeOptionalText(category);
        Tags = NormalizeTags(tags);
        writeGovernance?.Validate();
        WriteGovernance = writeGovernance;
        authentication?.Validate();
        Authentication = authentication;
        routing?.Validate();
        Routing = routing;
        setup?.Validate();
        Setup = setup;
        handoff?.Validate();
        Handoff = handoff;
        recovery?.Validate();
        Recovery = recovery;
        Aliases = NormalizeAliases(aliases, Name);
        AliasOf = string.IsNullOrWhiteSpace(aliasOf) ? null : aliasOf!.Trim();
    }

    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the tool description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Gets the JSON schema for tool parameters.
    /// </summary>
    public JsonObject? Parameters { get; }
    /// <summary>
    /// Gets optional human-friendly display name.
    /// </summary>
    public string? DisplayName { get; }
    /// <summary>
    /// Gets optional tool category label.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Gets optional tags associated with this tool definition.
    /// Tags are normalized to distinct deterministic ordering (ordinal-ignore-case).
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Gets optional aliases exposed for this tool definition.
    /// </summary>
    public IReadOnlyList<ToolAliasDefinition> Aliases { get; }

    /// <summary>
    /// Gets optional write-governance contract for mutating tools.
    /// </summary>
    public ToolWriteGovernanceContract? WriteGovernance { get; }

    /// <summary>
    /// Gets optional authentication contract for tools that require/declare auth behavior.
    /// </summary>
    public ToolAuthenticationContract? Authentication { get; }

    /// <summary>
    /// Gets optional routing contract for host-side orchestration.
    /// </summary>
    public ToolRoutingContract? Routing { get; }

    /// <summary>
    /// Gets optional setup contract for prerequisites and setup hints.
    /// </summary>
    public ToolSetupContract? Setup { get; }

    /// <summary>
    /// Gets optional handoff contract for cross-pack argument mappings.
    /// </summary>
    public ToolHandoffContract? Handoff { get; }

    /// <summary>
    /// Gets optional recovery contract for tool-owned resilience behavior.
    /// </summary>
    public ToolRecoveryContract? Recovery { get; }

    /// <summary>
    /// Gets the canonical tool name when this definition represents an alias.
    /// </summary>
    public string? AliasOf { get; }

    /// <summary>
    /// Gets the canonical tool name for this definition.
    /// </summary>
    public string CanonicalName => AliasOf ?? Name;

    internal static IDisposable RegisterMalformedTaxonomyTagDroppedObserver(Action<string> observer) {
        if (observer is null) {
            throw new ArgumentNullException(nameof(observer));
        }

        var previous = MalformedTaxonomyTagDroppedObserver.Value;
        MalformedTaxonomyTagDroppedObserver.Value = observer;
        return new MalformedTaxonomyTagDroppedObserverScope(previous);
    }

    /// <summary>
    /// Creates an alias definition derived from the current canonical definition.
    /// </summary>
    /// <param name="aliasName">Alias tool name.</param>
    /// <param name="description">Optional alias-specific description override.</param>
    /// <param name="tags">Optional alias tags merged with canonical tags.</param>
    public ToolDefinition CreateAliasDefinition(string aliasName, string? description = null, IReadOnlyList<string>? tags = null) {
        if (string.IsNullOrWhiteSpace(aliasName)) {
            throw new ArgumentException("Alias name cannot be empty.", nameof(aliasName));
        }

        var normalizedAliasName = aliasName.Trim();
        if (string.Equals(normalizedAliasName, CanonicalName, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Alias name cannot match canonical tool name.", nameof(aliasName));
        }

        var mergedTags = MergeTags(baseTags: Tags, overrideTags: tags);
        return new ToolDefinition(
            name: normalizedAliasName,
            description: string.IsNullOrWhiteSpace(description) ? Description : description,
            parameters: Parameters,
            displayName: null,
            category: Category,
            tags: mergedTags,
            writeGovernance: WriteGovernance,
            aliases: null,
            aliasOf: CanonicalName,
            authentication: Authentication,
            routing: Routing,
            setup: Setup,
            handoff: Handoff,
            recovery: Recovery);
    }

    /// <summary>
    /// Returns description text augmented with tag hints.
    /// </summary>
    public string? GetDescriptionWithTags() {
        var baseDescription = string.IsNullOrWhiteSpace(Description) ? null : Description!.Trim();
        if (Tags.Count == 0) {
            return baseDescription;
        }

        var tagsText = $"tags: {string.Join(", ", Tags)}";
        return string.IsNullOrWhiteSpace(baseDescription)
            ? tagsText
            : $"{baseDescription} [{tagsText}]";
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) {
        return MergeTags(baseTags: Array.Empty<string>(), overrideTags: tags);
    }

    private static IReadOnlyList<ToolAliasDefinition> NormalizeAliases(
        IReadOnlyList<ToolAliasDefinition>? aliases,
        string canonicalName) {
        if (aliases is null || aliases.Count == 0) {
            return Array.Empty<ToolAliasDefinition>();
        }

        var list = new List<ToolAliasDefinition>(aliases.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases) {
            if (alias is null) {
                continue;
            }
            if (string.Equals(alias.Name, canonicalName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (seen.Add(alias.Name)) {
                list.Add(alias);
            }
        }

        return list.Count == 0 ? Array.Empty<ToolAliasDefinition>() : list.ToArray();
    }

    private static string? NormalizeOptionalText(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static IReadOnlyList<string> MergeTags(IReadOnlyList<string> baseTags, IReadOnlyList<string>? overrideTags) {
        if ((baseTags is null || baseTags.Count == 0) && (overrideTags is null || overrideTags.Count == 0)) {
            return Array.Empty<string>();
        }

        var merged = new List<string>((baseTags?.Count ?? 0) + (overrideTags?.Count ?? 0));
        var seen = new HashSet<string>(TagComparer);
        var taxonomyByKey = new Dictionary<string, string>(TagComparer);

        AddTags(baseTags, allowTaxonomyOverride: false, merged, seen, taxonomyByKey);
        AddTags(overrideTags, allowTaxonomyOverride: true, merged, seen, taxonomyByKey);

        return FinalizeMergedTags(merged, taxonomyByKey);
    }

    private static void AddTags(
        IReadOnlyList<string>? tags,
        bool allowTaxonomyOverride,
        List<string> merged,
        HashSet<string> seen,
        Dictionary<string, string> taxonomyByKey) {
        if (tags is null || tags.Count == 0) {
            return;
        }

        foreach (var tag in tags) {
            AddTag(tag, allowTaxonomyOverride, merged, seen, taxonomyByKey);
        }
    }

    private static void AddTag(
        string? tag,
        bool allowTaxonomyOverride,
        List<string> merged,
        HashSet<string> seen,
        Dictionary<string, string> taxonomyByKey) {
        if (tag is null) {
            return;
        }

        var normalized = tag.Trim();
        if (normalized.Length == 0) {
            return;
        }

        if (ToolRoutingTaxonomy.TryGetTagKeyValue(normalized, out var taxonomyKey, out _)) {
            if (allowTaxonomyOverride || !taxonomyByKey.ContainsKey(taxonomyKey)) {
                taxonomyByKey[taxonomyKey] = normalized;
            }
            return;
        }
        if (ToolRoutingTaxonomy.IsTaxonomyTag(normalized)) {
            OnMalformedTaxonomyTagDropped(normalized);
            return;
        }

        if (seen.Add(normalized)) {
            merged.Add(normalized);
        }
    }

    private static IReadOnlyList<string> FinalizeMergedTags(List<string> merged, Dictionary<string, string> taxonomyByKey) {
        if (taxonomyByKey.Count > 0) {
            foreach (var taxonomyTag in taxonomyByKey.Values) {
                merged.Add(taxonomyTag);
            }
        }

        if (merged.Count == 0) {
            return Array.Empty<string>();
        }

        merged.Sort(TagComparer);
        return merged.ToArray();
    }

    private static void OnMalformedTaxonomyTagDropped(string tag) {
        var observer = MalformedTaxonomyTagDroppedObserver.Value;
        if (observer is null) {
            return;
        }

        try {
            observer(tag);
        } catch {
            // Diagnostics observers must never influence normalization behavior.
        }
    }

    private sealed class MalformedTaxonomyTagDroppedObserverScope : IDisposable {
        private readonly Action<string>? _previous;
        private bool _disposed;

        public MalformedTaxonomyTagDroppedObserverScope(Action<string>? previous) {
            _previous = previous;
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            MalformedTaxonomyTagDroppedObserver.Value = _previous;
            _disposed = true;
        }
    }
}
