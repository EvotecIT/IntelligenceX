using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Schema-derived routing traits projected from a registered tool definition.
/// </summary>
/// <param name="SupportsTableViewProjection">Indicates table-view projection arguments are present.</param>
/// <param name="TargetScopeArguments">Canonical target-scope arguments present in the schema.</param>
/// <param name="RemoteHostArguments">Canonical remote-host targeting arguments present in the schema.</param>
public readonly record struct ToolSchemaTraits(
    bool SupportsTableViewProjection,
    IReadOnlyList<string> TargetScopeArguments,
    IReadOnlyList<string> RemoteHostArguments) {

    /// <summary>
    /// Indicates whether the schema exposes any target-scope arguments.
    /// </summary>
    public bool SupportsTargetScoping => TargetScopeArguments?.Count > 0;

    /// <summary>
    /// Indicates whether the schema exposes any remote-host targeting arguments.
    /// </summary>
    public bool SupportsRemoteHostTargeting => RemoteHostArguments?.Count > 0;
}

/// <summary>
/// Shared projection helpers that derive routing traits from tool schemas.
/// </summary>
public static class ToolSchemaTraitProjection {
    private static readonly string[] TableViewArgumentNames = { "columns", "sort_by", "sort_direction", "top" };
    private static readonly string[] TargetScopeArgumentNames = {
        "domain_name",
        "forest_name",
        "domain_controller",
        "search_base_dn",
        "path",
        "folder",
        "channel",
        "provider_name",
        "computer_name",
        "machine_name",
        "machine_names",
        "server"
    };
    private static readonly string[] RemoteHostArgumentNames = {
        "computer_name",
        "machine_name",
        "machine_names",
        "domain_controller",
        "server",
        "targets"
    };

    /// <summary>
    /// Projects schema-derived traits from a tool definition.
    /// </summary>
    /// <param name="definition">Tool definition to inspect.</param>
    /// <returns>Normalized schema traits.</returns>
    public static ToolSchemaTraits Project(ToolDefinition? definition) {
        _ = ReadPropertyNames(definition, maxCount: 0, out var traits);
        return traits;
    }

    /// <summary>
    /// Reads normalized schema property names and derived traits from a tool definition.
    /// </summary>
    /// <param name="definition">Tool definition to inspect.</param>
    /// <param name="maxCount">Maximum number of property names to return. Use zero to return no names and only project traits.</param>
    /// <param name="traits">Projected schema traits.</param>
    /// <returns>Normalized property names up to <paramref name="maxCount"/>.</returns>
    public static string[] ReadPropertyNames(ToolDefinition? definition, int maxCount, out ToolSchemaTraits traits) {
        traits = default;
        if (definition?.Parameters is null) {
            return Array.Empty<string>();
        }

        var properties = definition.Parameters.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        var allNames = new List<string>(properties.Count);
        var selectedNames = maxCount > 0 ? new List<string>(Math.Min(maxCount, properties.Count)) : null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties) {
            var name = NormalizeSchemaToken(kv.Key);
            if (name.Length == 0 || !seen.Add(name)) {
                continue;
            }

            allNames.Add(name);
            if (selectedNames is not null && selectedNames.Count < maxCount) {
                selectedNames.Add(name);
            }
        }

        traits = new ToolSchemaTraits(
            SupportsTableViewProjection: ContainsKnownArgument(allNames, TableViewArgumentNames),
            TargetScopeArguments: IntersectKnownArguments(allNames, TargetScopeArgumentNames),
            RemoteHostArguments: IntersectKnownArguments(allNames, RemoteHostArgumentNames));

        return selectedNames is null || selectedNames.Count == 0
            ? Array.Empty<string>()
            : selectedNames.ToArray();
    }

    /// <summary>
    /// Reads normalized required-argument names from a tool definition.
    /// </summary>
    /// <param name="definition">Tool definition to inspect.</param>
    /// <param name="maxCount">Maximum number of required argument names to return.</param>
    /// <returns>Normalized required-argument names up to <paramref name="maxCount"/>.</returns>
    public static string[] ReadRequiredNames(ToolDefinition? definition, int maxCount) {
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var required = definition.Parameters.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(Math.Min(maxCount, required.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < required.Count && names.Count < maxCount; i++) {
            var value = NormalizeSchemaToken(required[i]?.AsString());
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }

            names.Add(value);
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    /// <summary>
    /// Builds a compact planner-facing summary of the provided schema traits.
    /// </summary>
    /// <param name="traits">Schema traits to summarize.</param>
    /// <returns>Compact trait summary suitable for planner prompts.</returns>
    public static string BuildTraitSummary(in ToolSchemaTraits traits) {
        var values = new List<string>(capacity: 3);
        if (traits.SupportsTableViewProjection) {
            values.Add("table_view_projection");
        }

        if (traits.SupportsTargetScoping) {
            values.Add(BuildTraitValue("target_scoping", traits.TargetScopeArguments));
        }

        if (traits.SupportsRemoteHostTargeting) {
            values.Add(BuildTraitValue("remote_host_targeting", traits.RemoteHostArguments));
        }

        return values.Count == 0 ? string.Empty : string.Join(", ", values);
    }

    /// <summary>
    /// Builds additional lexical search text from the provided schema traits.
    /// </summary>
    /// <param name="traits">Schema traits to surface in routing search text.</param>
    /// <returns>Additional search text tokens.</returns>
    public static string BuildRoutingSearchAugmentation(in ToolSchemaTraits traits) {
        if (!traits.SupportsTargetScoping && !traits.SupportsRemoteHostTargeting && !traits.SupportsTableViewProjection) {
            return string.Empty;
        }

        var sb = new StringBuilder(128);
        if (traits.SupportsTableViewProjection) {
            sb.Append(" table view projection columns sort_by sort_direction top");
        }

        if (traits.SupportsTargetScoping) {
            sb.Append(" target scoping target_scope");
            AppendArgumentNames(sb, traits.TargetScopeArguments);
        }

        if (traits.SupportsRemoteHostTargeting) {
            sb.Append(" remote host remote_host_targeting target_host");
            AppendArgumentNames(sb, traits.RemoteHostArguments);
        }

        return sb.ToString();
    }

    private static string BuildTraitValue(string name, IReadOnlyList<string> arguments) {
        if (arguments is null || arguments.Count == 0) {
            return name;
        }

        return $"{name}({string.Join(", ", arguments)})";
    }

    private static void AppendArgumentNames(StringBuilder sb, IReadOnlyList<string> arguments) {
        if (arguments is null || arguments.Count == 0) {
            return;
        }

        for (var i = 0; i < arguments.Count; i++) {
            var argument = (arguments[i] ?? string.Empty).Trim();
            if (argument.Length == 0) {
                continue;
            }

            sb.Append(' ').Append(argument);
        }
    }

    private static bool ContainsKnownArgument(IReadOnlyList<string> names, IReadOnlyList<string> knownNames) {
        return IntersectKnownArguments(names, knownNames).Count > 0;
    }

    private static IReadOnlyList<string> IntersectKnownArguments(IReadOnlyList<string> names, IReadOnlyList<string> knownNames) {
        if (names is null || names.Count == 0 || knownNames is null || knownNames.Count == 0) {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (var i = 0; i < knownNames.Count; i++) {
            var known = (knownNames[i] ?? string.Empty).Trim();
            if (known.Length == 0 || !set.Contains(known)) {
                continue;
            }

            result.Add(known);
        }

        return result.Count == 0 ? Array.Empty<string>() : result;
    }

    private static string NormalizeSchemaToken(string? token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '-') {
                sb.Append(c);
            } else if (char.IsWhiteSpace(c)) {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }
}
