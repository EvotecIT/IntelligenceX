using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Reflection-free read contracts for trimmed and native view clients.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LedgerClientSettings))]
[JsonSerializable(typeof(LedgerWorkView))]
[JsonSerializable(typeof(ProjectContextView))]
[JsonSerializable(typeof(ProjectContextView[]))]
[JsonSerializable(typeof(ProjectDirectoryView))]
[JsonSerializable(typeof(KnowledgeSearchView))]
[JsonSerializable(typeof(LedgerItemView))]
public partial class LedgerReadJson : JsonSerializerContext;
