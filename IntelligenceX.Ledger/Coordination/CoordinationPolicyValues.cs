using System.Text.Json;
using System.Text.Json.Nodes;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Policy path editing and value history shared by the command endpoints.</summary>
internal static class CoordinationPolicyValues {
    internal static CoordinationPolicy Set(CoordinationPolicy policy, PolicyValueCommand command) {
        CoordinationValidation.Require(command.Path is { Length: > 0 and <= 256 } &&
            command.Path != "revision" && command.Value.ValueKind != JsonValueKind.Undefined, "invalid-policy-path");
        var document = JsonSerializer.SerializeToNode(policy, LedgerReadJson.Default.CoordinationPolicy)!.AsObject();
        var original = document.DeepClone();
        JsonNode current = document;
        var remaining = command.Path;
        while (true) {
            if (current is JsonArray machines) {
                // Machine identifiers can contain dots. Match the registered identifier as a unit.
                var match = machines.OfType<JsonObject>().Select(machine => new {
                    Machine = machine, Id = machine["id"]?.GetValue<string>()
                }).Where(value => value.Id is not null && remaining.StartsWith(value.Id + ".", StringComparison.Ordinal))
                    .OrderByDescending(value => value.Id!.Length).FirstOrDefault();
                CoordinationValidation.Require(match is not null, "unknown-policy-machine");
                remaining = remaining[(match.Id!.Length + 1)..];
                current = match.Machine;
                continue;
            }
            CoordinationValidation.Require(current is JsonObject, "invalid-policy-path");
            var properties = (JsonObject)current;
            var key = properties.Select(pair => pair.Key).Where(name =>
                remaining == name || remaining.StartsWith(name + ".", StringComparison.Ordinal))
                .OrderByDescending(name => name.Length).FirstOrDefault();
            if (key is null) {
                // Typed deserialization rejects unknown record members. Dictionaries accept new keys.
                properties[remaining] = JsonNode.Parse(command.Value.GetRawText());
                break;
            }
            if (remaining == key) {
                properties[key] = JsonNode.Parse(command.Value.GetRawText());
                break;
            }
            remaining = remaining[(key.Length + 1)..];
            CoordinationValidation.Require(properties[key] is not null, "invalid-policy-path");
            current = properties[key]!;
        }
        if (JsonNode.DeepEquals(original, document)) return policy;
        document["revision"] = checked(policy.Revision + 1);
        return document.Deserialize<CoordinationPolicy>(new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new CoordinationConflictException("invalid-policy");
    }

    internal static PolicyValueChange[] Changes(CoordinationPolicy previous, CoordinationPolicy updated) {
        var changes = new List<PolicyValueChange>();
        Compare(JsonSerializer.SerializeToNode(previous, LedgerReadJson.Default.CoordinationPolicy),
            JsonSerializer.SerializeToNode(updated, LedgerReadJson.Default.CoordinationPolicy), "");
        return changes.ToArray();

        void Compare(JsonNode? before, JsonNode? after, string path) {
            if (JsonNode.DeepEquals(before, after)) return;
            if (before is JsonObject oldObject && after is JsonObject newObject) {
                foreach (var name in oldObject.Select(pair => pair.Key).Union(newObject.Select(pair => pair.Key)).Order(StringComparer.Ordinal))
                    Compare(oldObject[name], newObject[name], path.Length == 0 ? name : path + "." + name);
                return;
            }
            if (path == "machines" && before is JsonArray oldMachines && after is JsonArray newMachines) {
                var oldById = oldMachines.OfType<JsonObject>().ToDictionary(machine => machine["id"]!.GetValue<string>(), StringComparer.Ordinal);
                var newById = newMachines.OfType<JsonObject>().ToDictionary(machine => machine["id"]!.GetValue<string>(), StringComparer.Ordinal);
                foreach (var id in oldById.Keys.Union(newById.Keys).Order(StringComparer.Ordinal))
                    Compare(oldById.GetValueOrDefault(id), newById.GetValueOrDefault(id), path + "." + id);
                return;
            }
            changes.Add(new(path, JsonSerializer.SerializeToElement(before), JsonSerializer.SerializeToElement(after)));
        }
    }
}
