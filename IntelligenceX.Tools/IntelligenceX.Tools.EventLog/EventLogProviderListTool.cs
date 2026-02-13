using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Lists Windows Event Log providers on the local machine.
/// </summary>
public sealed class EventLogProviderListTool : EventLogToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_providers_list",
        "List Windows Event Log providers available on this machine (read-only, capped).",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive name filter.")),
                ("max_providers", ToolSchema.Integer("Optional maximum providers to return (capped).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogProviderListTool"/> class.
    /// </summary>
    public EventLogProviderListTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return Task.FromResult(RunCatalogNameList(
            arguments: arguments,
            providers: true,
            maxArgumentName: "max_providers",
            title: "Event Log providers (preview)",
            rowsPath: "providers",
            header: "Provider",
            columnName: "name",
            cancellationToken: cancellationToken));
    }
}
