using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools;

/// <summary>
/// Describes an alias exposed for a tool definition.
/// </summary>
public sealed class ToolAliasDefinition {
    /// <summary>
    /// Initializes a new alias definition.
    /// </summary>
    /// <param name="name">Alias tool name.</param>
    /// <param name="description">Optional alias-specific description override.</param>
    /// <param name="tags">Optional alias tags merged with canonical tool tags.</param>
    public ToolAliasDefinition(string name, string? description = null, IReadOnlyList<string>? tags = null) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Alias name cannot be empty.", nameof(name));
        }

        Name = name.Trim();
        Description = description;
        Tags = NormalizeTags(tags);
    }

    /// <summary>
    /// Gets the alias name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the alias-specific description override.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets alias tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) {
        if (tags is null || tags.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(tags.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }

            var normalized = tag.Trim();
            if (seen.Add(normalized)) {
                list.Add(normalized);
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }
}
