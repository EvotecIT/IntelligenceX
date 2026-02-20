using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Defines a tool that can be invoked by the model.
/// </summary>
public sealed class ToolDefinition {
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
        ToolAuthenticationContract? authentication = null) {
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
    /// Gets the canonical tool name when this definition represents an alias.
    /// </summary>
    public string? AliasOf { get; }

    /// <summary>
    /// Gets the canonical tool name for this definition.
    /// </summary>
    public string CanonicalName => AliasOf ?? Name;

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

        var mergedTags = MergeTags(Tags, tags);
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
            authentication: Authentication);
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
        if (tags is null || tags.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(tags.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTaxonomyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }

            var normalized = tag.Trim();
            if (ToolRoutingTaxonomy.TryGetTagKey(normalized, out var taxonomyKey) &&
                !seenTaxonomyKeys.Add(taxonomyKey)) {
                continue;
            }

            if (seen.Add(normalized)) {
                list.Add(normalized);
            }
        }

        if (list.Count == 0) {
            return Array.Empty<string>();
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
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

    private static IReadOnlyList<string> MergeTags(IReadOnlyList<string> first, IReadOnlyList<string>? second) {
        if ((first is null || first.Count == 0) && (second is null || second.Count == 0)) {
            return Array.Empty<string>();
        }

        var merged = new List<string>((first?.Count ?? 0) + (second?.Count ?? 0));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTaxonomyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (first is not null) {
            foreach (var tag in first) {
                if (string.IsNullOrWhiteSpace(tag)) {
                    continue;
                }
                var normalized = tag.Trim();
                if (ToolRoutingTaxonomy.TryGetTagKey(normalized, out var taxonomyKey) &&
                    !seenTaxonomyKeys.Add(taxonomyKey)) {
                    continue;
                }

                if (seen.Add(normalized)) {
                    merged.Add(normalized);
                }
            }
        }

        if (second is not null) {
            foreach (var tag in second) {
                if (string.IsNullOrWhiteSpace(tag)) {
                    continue;
                }
                var normalized = tag.Trim();
                if (ToolRoutingTaxonomy.TryGetTagKey(normalized, out var taxonomyKey) &&
                    !seenTaxonomyKeys.Add(taxonomyKey)) {
                    continue;
                }

                if (seen.Add(normalized)) {
                    merged.Add(normalized);
                }
            }
        }

        if (merged.Count == 0) {
            return Array.Empty<string>();
        }

        merged.Sort(StringComparer.OrdinalIgnoreCase);
        return merged.ToArray();
    }
}
