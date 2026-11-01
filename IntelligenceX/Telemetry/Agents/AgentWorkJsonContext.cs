using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>Generated JSON contract for reflection-free snapshot exchange, including Native AOT consumers.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentWorkSnapshot))]
public partial class AgentWorkJsonContext : JsonSerializerContext {
}

/// <summary>Snapshot JSON using generated metadata on all target frameworks.</summary>
public static class AgentWorkJson {
    /// <summary>Serializes only the declared snapshot contract; never uses reflection-based fallback.</summary>
    public static string Serialize(AgentWorkSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, AgentWorkJsonContext.Default.AgentWorkSnapshot);
}
