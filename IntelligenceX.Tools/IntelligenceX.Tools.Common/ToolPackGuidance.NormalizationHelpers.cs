using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

public static partial class ToolPackGuidance {
    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string>? values, bool distinct = true) {
        var query = (values ?? Array.Empty<string>())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim());

        if (distinct) {
            query = query.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        return query.ToArray();
    }

    private static IReadOnlyList<ToolPackFlowStepModel> NormalizeFlowSteps(IEnumerable<ToolPackFlowStepModel>? flowSteps) {
        var list = new List<ToolPackFlowStepModel>();

        foreach (var step in flowSteps ?? Array.Empty<ToolPackFlowStepModel>()) {
            if (step is null || string.IsNullOrWhiteSpace(step.Goal)) {
                continue;
            }

            list.Add(new ToolPackFlowStepModel {
                Goal = step.Goal.Trim(),
                SuggestedTools = NormalizeValues(step.SuggestedTools),
                Notes = string.IsNullOrWhiteSpace(step.Notes) ? null : step.Notes.Trim()
            });
        }

        return list;
    }

    private static IReadOnlyList<ToolPackCapabilityModel> NormalizeCapabilities(IEnumerable<ToolPackCapabilityModel>? capabilities) {
        var list = new List<ToolPackCapabilityModel>();

        foreach (var capability in capabilities ?? Array.Empty<ToolPackCapabilityModel>()) {
            if (capability is null || string.IsNullOrWhiteSpace(capability.Id) || string.IsNullOrWhiteSpace(capability.Summary)) {
                continue;
            }

            list.Add(new ToolPackCapabilityModel {
                Id = capability.Id.Trim(),
                Summary = capability.Summary.Trim(),
                PrimaryTools = NormalizeValues(capability.PrimaryTools),
                Notes = string.IsNullOrWhiteSpace(capability.Notes) ? null : capability.Notes.Trim()
            });
        }

        return list;
    }

    private static IReadOnlyList<ToolPackEntityHandoffModel> NormalizeEntityHandoffs(IEnumerable<ToolPackEntityHandoffModel>? handoffs) {
        var list = new List<ToolPackEntityHandoffModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var handoff in handoffs ?? Array.Empty<ToolPackEntityHandoffModel>()) {
            if (handoff is null || string.IsNullOrWhiteSpace(handoff.Id) || string.IsNullOrWhiteSpace(handoff.Summary)) {
                continue;
            }

            var id = handoff.Id.Trim();
            if (!seen.Add(id)) {
                continue;
            }

            list.Add(new ToolPackEntityHandoffModel {
                Id = id,
                Summary = handoff.Summary.Trim(),
                EntityKinds = NormalizeValues(handoff.EntityKinds),
                SourceTools = NormalizeValues(handoff.SourceTools),
                TargetTools = NormalizeValues(handoff.TargetTools),
                FieldMappings = NormalizeEntityFieldMappings(handoff.FieldMappings),
                Notes = string.IsNullOrWhiteSpace(handoff.Notes) ? null : handoff.Notes.Trim()
            });
        }

        return list;
    }

    private static IReadOnlyList<ToolPackEntityFieldMappingModel> NormalizeEntityFieldMappings(IEnumerable<ToolPackEntityFieldMappingModel>? mappings) {
        var list = new List<ToolPackEntityFieldMappingModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings ?? Array.Empty<ToolPackEntityFieldMappingModel>()) {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.SourceField) || string.IsNullOrWhiteSpace(mapping.TargetArgument)) {
                continue;
            }

            var source = mapping.SourceField.Trim();
            var target = mapping.TargetArgument.Trim();
            var key = $"{source}|{target}";
            if (!seen.Add(key)) {
                continue;
            }

            list.Add(new ToolPackEntityFieldMappingModel {
                SourceField = source,
                TargetArgument = target,
                Normalization = string.IsNullOrWhiteSpace(mapping.Normalization) ? null : mapping.Normalization.Trim()
            });
        }

        return list;
    }

    private static IReadOnlyList<ToolPackToolCatalogEntryModel> NormalizeToolCatalog(IEnumerable<ToolPackToolCatalogEntryModel>? entries) {
        var list = new List<ToolPackToolCatalogEntryModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries ?? Array.Empty<ToolPackToolCatalogEntryModel>()) {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Name)) {
                continue;
            }

            var name = entry.Name.Trim();
            if (!seen.Add(name)) {
                continue;
            }

            list.Add(new ToolPackToolCatalogEntryModel {
                Name = name,
                Description = entry.Description?.Trim() ?? string.Empty,
                RequiredArguments = NormalizeValues(entry.RequiredArguments),
                Arguments = NormalizeArguments(entry.Arguments),
                SupportsTableViewProjection = entry.SupportsTableViewProjection,
                IsPackInfoTool = entry.IsPackInfoTool,
                Traits = NormalizeTraits(entry.Traits),
                IsWriteCapable = entry.IsWriteCapable,
                RequiresWriteGovernance = entry.RequiresWriteGovernance,
                WriteGovernanceContractId = string.IsNullOrWhiteSpace(entry.WriteGovernanceContractId)
                    ? null
                    : entry.WriteGovernanceContractId.Trim(),
                IsAuthenticationAware = entry.IsAuthenticationAware,
                RequiresAuthentication = entry.RequiresAuthentication,
                AuthenticationContractId = string.IsNullOrWhiteSpace(entry.AuthenticationContractId)
                    ? null
                    : entry.AuthenticationContractId.Trim(),
                AuthenticationMode = string.IsNullOrWhiteSpace(entry.AuthenticationMode)
                    ? null
                    : entry.AuthenticationMode.Trim(),
                AuthenticationArguments = NormalizeValues(entry.AuthenticationArguments),
                SupportsConnectivityProbe = entry.SupportsConnectivityProbe,
                ProbeToolName = string.IsNullOrWhiteSpace(entry.ProbeToolName)
                    ? null
                    : entry.ProbeToolName.Trim()
            });
        }

        return list;
    }

    private static ToolPackToolTraitsModel BuildToolTraits(IEnumerable<string>? argumentNames, bool supportsTableViewProjection) {
        var names = NormalizeValues(argumentNames);

        var projectionArguments = IntersectKnownArguments(names, TableViewArgumentNames);
        var pagingArguments = IntersectKnownArguments(names, PagingArgumentNames);
        var timeRangeArguments = IntersectKnownArguments(names, TimeRangeArgumentNames);
        var dynamicAttributeArguments = IntersectKnownArguments(names, DynamicAttributeArgumentNames);
        var targetScopeArguments = IntersectKnownArguments(names, TargetScopeArgumentNames);
        var mutatingActionArguments = IntersectKnownArguments(names, MutatingActionArgumentNames);
        var writeGovernanceMetadataArguments = IntersectKnownArguments(
            names,
            ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments);
        var authenticationArguments = IntersectKnownArguments(names, AuthenticationArgumentNames);

        return new ToolPackToolTraitsModel {
            SupportsTableViewProjection = supportsTableViewProjection,
            TableViewArguments = projectionArguments,
            SupportsPaging = pagingArguments.Count > 0,
            PagingArguments = pagingArguments,
            SupportsTimeRange = timeRangeArguments.Count > 0,
            TimeRangeArguments = timeRangeArguments,
            SupportsDynamicAttributes = dynamicAttributeArguments.Count > 0,
            DynamicAttributeArguments = dynamicAttributeArguments,
            SupportsTargetScoping = targetScopeArguments.Count > 0,
            TargetScopeArguments = targetScopeArguments,
            SupportsMutatingActions = mutatingActionArguments.Count > 0,
            MutatingActionArguments = mutatingActionArguments,
            SupportsWriteGovernanceMetadata = writeGovernanceMetadataArguments.Count > 0,
            WriteGovernanceMetadataArguments = writeGovernanceMetadataArguments,
            SupportsAuthentication = authenticationArguments.Count > 0,
            AuthenticationArguments = authenticationArguments
        };
    }

    private static ToolPackToolTraitsModel NormalizeTraits(ToolPackToolTraitsModel? traits) {
        if (traits is null) {
            return new ToolPackToolTraitsModel();
        }

        var projectionArguments = NormalizeValues(traits.TableViewArguments);
        var pagingArguments = NormalizeValues(traits.PagingArguments);
        var timeRangeArguments = NormalizeValues(traits.TimeRangeArguments);
        var dynamicAttributeArguments = NormalizeValues(traits.DynamicAttributeArguments);
        var targetScopeArguments = NormalizeValues(traits.TargetScopeArguments);
        var mutatingActionArguments = NormalizeValues(traits.MutatingActionArguments);
        var writeGovernanceMetadataArguments = NormalizeValues(traits.WriteGovernanceMetadataArguments);
        var authenticationArguments = NormalizeValues(traits.AuthenticationArguments);

        return new ToolPackToolTraitsModel {
            SupportsTableViewProjection = traits.SupportsTableViewProjection || projectionArguments.Count > 0,
            TableViewArguments = projectionArguments,
            SupportsPaging = traits.SupportsPaging || pagingArguments.Count > 0,
            PagingArguments = pagingArguments,
            SupportsTimeRange = traits.SupportsTimeRange || timeRangeArguments.Count > 0,
            TimeRangeArguments = timeRangeArguments,
            SupportsDynamicAttributes = traits.SupportsDynamicAttributes || dynamicAttributeArguments.Count > 0,
            DynamicAttributeArguments = dynamicAttributeArguments,
            SupportsTargetScoping = traits.SupportsTargetScoping || targetScopeArguments.Count > 0,
            TargetScopeArguments = targetScopeArguments,
            SupportsMutatingActions = traits.SupportsMutatingActions || mutatingActionArguments.Count > 0,
            MutatingActionArguments = mutatingActionArguments,
            SupportsWriteGovernanceMetadata = traits.SupportsWriteGovernanceMetadata || writeGovernanceMetadataArguments.Count > 0,
            WriteGovernanceMetadataArguments = writeGovernanceMetadataArguments,
            SupportsAuthentication = traits.SupportsAuthentication || authenticationArguments.Count > 0,
            AuthenticationArguments = authenticationArguments
        };
    }

    private static IReadOnlyList<ToolPackToolArgumentModel> NormalizeArguments(IEnumerable<ToolPackToolArgumentModel>? arguments) {
        var list = new List<ToolPackToolArgumentModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in arguments ?? Array.Empty<ToolPackToolArgumentModel>()) {
            if (argument is null || string.IsNullOrWhiteSpace(argument.Name)) {
                continue;
            }

            var name = argument.Name.Trim();
            if (!seen.Add(name)) {
                continue;
            }

            list.Add(new ToolPackToolArgumentModel {
                Name = name,
                Type = string.IsNullOrWhiteSpace(argument.Type) ? "unknown" : argument.Type.Trim(),
                Required = argument.Required,
                Description = string.IsNullOrWhiteSpace(argument.Description) ? null : argument.Description.Trim(),
                EnumValues = NormalizeValues(argument.EnumValues)
            });
        }

        return list;
    }

    private static IReadOnlyList<string> ReadRequiredArguments(JsonObject? schema) {
        if (schema is null) {
            return Array.Empty<string>();
        }

        var required = schema.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        return ToolArgs.ReadDistinctStringArray(required);
    }

    private static IReadOnlyList<ToolPackToolArgumentModel> ReadArgumentHints(JsonObject? schema, IReadOnlyList<string> requiredArguments) {
        var properties = schema?.GetObject("properties");
        if (properties is null) {
            return Array.Empty<ToolPackToolArgumentModel>();
        }

        var requiredSet = new HashSet<string>(requiredArguments ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var keys = GetObjectKeys(properties);
        if (keys.Count == 0) {
            return Array.Empty<ToolPackToolArgumentModel>();
        }

        var list = new List<ToolPackToolArgumentModel>(keys.Count);
        for (var i = 0; i < keys.Count; i++) {
            var key = keys[i];
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            var propertySchema = properties.GetObject(key);
            if (propertySchema is null) {
                continue;
            }

            var type = ReadArgumentType(propertySchema);
            var enumValues = ToolArgs.ReadDistinctStringArray(propertySchema.GetArray("enum"));
            list.Add(new ToolPackToolArgumentModel {
                Name = key.Trim(),
                Type = type,
                Required = requiredSet.Contains(key),
                Description = ToolArgs.GetOptionalTrimmed(propertySchema, "description"),
                EnumValues = enumValues
            });
        }

        return list;
    }

    private static string ReadArgumentType(JsonObject schema) {
        var type = ToolArgs.GetOptionalTrimmed(schema, "type");
        if (string.IsNullOrWhiteSpace(type)) {
            return "unknown";
        }

        if (!string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)) {
            return type;
        }

        var itemSchema = schema.GetObject("items");
        if (itemSchema is null) {
            return "array";
        }

        var itemType = ToolArgs.GetOptionalTrimmed(itemSchema, "type");
        return string.IsNullOrWhiteSpace(itemType) ? "array" : $"array<{itemType}>";
    }

    private static IReadOnlyList<string> GetObjectKeys(JsonObject obj) {
        var keysProperty = obj.GetType().GetProperty("Keys");
        if (keysProperty?.GetValue(obj) is IEnumerable<string> keys) {
            return keys
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .ToArray();
        }

        if (obj is System.Collections.IEnumerable enumerable) {
            var list = new List<string>();
            foreach (var item in enumerable) {
                if (item is null) {
                    continue;
                }

                var keyProperty = item.GetType().GetProperty("Key");
                var key = keyProperty?.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(key)) {
                    list.Add(key.Trim());
                }
            }

            return list;
        }

        return Array.Empty<string>();
    }

    private static bool SupportsTableViewProjection(JsonObject? schema) {
        var properties = schema?.GetObject("properties");
        if (properties is null) {
            return false;
        }

        return properties.TryGetValue("columns", out _)
               || properties.TryGetValue("sort_by", out _)
               || properties.TryGetValue("sort_direction", out _)
               || properties.TryGetValue("top", out _);
    }

    private static IReadOnlyList<string> IntersectKnownArguments(IEnumerable<string> names, IEnumerable<string> knownNames) {
        var set = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var known in knownNames ?? Array.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(known)) {
                continue;
            }

            if (set.Contains(known)) {
                result.Add(known);
            }
        }

        return result;
    }

    private static string? ToAuthenticationModeId(ToolAuthenticationContract? contract) {
        if (contract is null || !contract.IsAuthenticationAware) {
            return null;
        }

        return contract.Mode switch {
            ToolAuthenticationMode.None => "none",
            ToolAuthenticationMode.HostManaged => "host_managed",
            ToolAuthenticationMode.ProfileReference => "profile_reference",
            ToolAuthenticationMode.RunAsReference => "run_as_reference",
            _ => throw new InvalidOperationException(
                $"Unsupported authentication mode '{contract.Mode}' in tool authentication contract.")
        };
    }
}
