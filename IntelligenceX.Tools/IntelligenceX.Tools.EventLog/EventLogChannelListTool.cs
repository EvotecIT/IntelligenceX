using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Lists Windows Event Log channels on the local or remote machine.
/// </summary>
public sealed class EventLogChannelListTool : EventLogToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_channels_list",
        "List Windows Event Log channels available on this machine (or remote machine_name) (read-only, capped).",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("session_timeout_ms", ToolSchema.Integer("Optional session timeout in milliseconds for remote queries.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive name filter.")),
                ("max_channels", ToolSchema.Integer("Optional maximum channels to return (capped).")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogChannelListTool"/> class.
    /// </summary>
    public EventLogChannelListTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return Task.FromResult(RunCatalogNameList(
            arguments: arguments,
            providers: false,
            maxArgumentName: "max_channels",
            title: "Event Log channels (preview)",
            rowsPath: "channels",
            header: "Channel",
            columnName: "name",
            cancellationToken: cancellationToken));
    }
}
