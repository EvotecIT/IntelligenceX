using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared category-level routing defaults used when tool contracts do not declare explicit scope/entity values.
/// </summary>
public static class ToolCategoryRoutingSemantics {
    private sealed class CategoryRoutingDescriptor {
        public CategoryRoutingDescriptor(string category, string? defaultScope = null, string? defaultEntity = null) {
            Category = (category ?? string.Empty).Trim();
            DefaultScope = (defaultScope ?? string.Empty).Trim();
            DefaultEntity = (defaultEntity ?? string.Empty).Trim();
        }

        public string Category { get; }
        public string DefaultScope { get; }
        public string DefaultEntity { get; }
    }

    private static readonly IReadOnlyDictionary<string, CategoryRoutingDescriptor> DescriptorsByCategory =
        BuildDescriptorMap();

    /// <summary>
    /// Returns the default scope for a known category when one exists.
    /// </summary>
    public static bool TryGetDefaultScope(string? category, out string scope) {
        scope = string.Empty;
        if (!TryGetDescriptor(category, out var descriptor) || descriptor.DefaultScope.Length == 0) {
            return false;
        }

        scope = descriptor.DefaultScope;
        return true;
    }

    /// <summary>
    /// Returns the default entity for a known category when one exists.
    /// </summary>
    public static bool TryGetDefaultEntity(string? category, out string entity) {
        entity = string.Empty;
        if (!TryGetDescriptor(category, out var descriptor) || descriptor.DefaultEntity.Length == 0) {
            return false;
        }

        entity = descriptor.DefaultEntity;
        return true;
    }

    private static bool TryGetDescriptor(string? category, out CategoryRoutingDescriptor descriptor) {
        var normalized = (category ?? string.Empty).Trim();
        if (normalized.Length > 0 && DescriptorsByCategory.TryGetValue(normalized, out descriptor!)) {
            return true;
        }

        descriptor = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, CategoryRoutingDescriptor> BuildDescriptorMap() {
        var descriptors = new[] {
            new CategoryRoutingDescriptor(category: "active_directory", defaultScope: "domain", defaultEntity: "directory_object"),
            new CategoryRoutingDescriptor(category: "dns", defaultScope: "domain", defaultEntity: "dns"),
            new CategoryRoutingDescriptor(category: "email", defaultScope: "message", defaultEntity: "message"),
            new CategoryRoutingDescriptor(category: "eventlog", defaultScope: "host", defaultEntity: "event"),
            new CategoryRoutingDescriptor(category: "filesystem", defaultScope: "file", defaultEntity: "file"),
            new CategoryRoutingDescriptor(category: "officeimo", defaultScope: "file", defaultEntity: "file"),
            new CategoryRoutingDescriptor(category: "powershell", defaultScope: "host", defaultEntity: "command"),
            new CategoryRoutingDescriptor(category: "reviewer_setup", defaultScope: "repository"),
            new CategoryRoutingDescriptor(category: "system", defaultScope: "host", defaultEntity: "host")
        };

        var map = new Dictionary<string, CategoryRoutingDescriptor>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < descriptors.Length; i++) {
            var descriptor = descriptors[i];
            if (descriptor.Category.Length == 0 || map.ContainsKey(descriptor.Category)) {
                continue;
            }

            map[descriptor.Category] = descriptor;
        }

        return map;
    }
}
