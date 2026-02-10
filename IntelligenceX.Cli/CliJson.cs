using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IntelligenceX.Cli;

internal static class CliJson {
    internal static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}

