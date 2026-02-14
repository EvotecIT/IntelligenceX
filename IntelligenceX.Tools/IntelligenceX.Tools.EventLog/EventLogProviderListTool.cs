using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Lists Windows Event Log providers on the local or remote machine.
/// </summary>
public sealed class EventLogProviderListTool : EventLogToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_providers_list",
        "List Windows Event Log providers available on this machine (or remote machine_name) (read-only, capped).",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("session_timeout_ms", ToolSchema.Integer("Optional session timeout in milliseconds for remote queries.")),
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
