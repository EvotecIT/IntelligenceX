using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Features;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists Windows optional/server features (read-only, capped).
/// </summary>
public sealed class SystemFeaturesListTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, FeatureInventorySource> SourceByName =
        new Dictionary<string, FeatureInventorySource>(StringComparer.OrdinalIgnoreCase) {
            ["any"] = FeatureInventorySource.Any,
            ["optional"] = FeatureInventorySource.Optional,
            ["server"] = FeatureInventorySource.Server
        };

    private static readonly IReadOnlyDictionary<FeatureInventorySource, string> SourceNames =
        new Dictionary<FeatureInventorySource, string> {
            [FeatureInventorySource.Any] = "any",
            [FeatureInventorySource.Optional] = "optional",
            [FeatureInventorySource.Server] = "server"
        };

    private static readonly IReadOnlyDictionary<string, WindowsOptionalFeatureState> OptionalStateByName =
        new Dictionary<string, WindowsOptionalFeatureState>(StringComparer.OrdinalIgnoreCase) {
            ["enabled"] = WindowsOptionalFeatureState.Enabled,
            ["disabled"] = WindowsOptionalFeatureState.Disabled,
            ["absent"] = WindowsOptionalFeatureState.Absent,
            ["unknown"] = WindowsOptionalFeatureState.Unknown
        };

    private static readonly IReadOnlyDictionary<WindowsOptionalFeatureState, string> OptionalStateNames =
        new Dictionary<WindowsOptionalFeatureState, string> {
            [WindowsOptionalFeatureState.Enabled] = "enabled",
            [WindowsOptionalFeatureState.Disabled] = "disabled",
            [WindowsOptionalFeatureState.Absent] = "absent",
            [WindowsOptionalFeatureState.Unknown] = "unknown"
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_features_list",
        "List Windows optional/server features (read-only, capped).",
        ToolSchema.Object(
                ("source", ToolSchema.String("Feature source filter. Default any.").Enum("any", "optional", "server")),
                ("name_contains", ToolSchema.String("Optional case-insensitive name/display-name filter.")),
                ("optional_state", ToolSchema.String("Optional state filter for optional features.").Enum("any", "enabled", "disabled", "absent", "unknown")),
                ("max_entries", ToolSchema.Integer("Optional maximum rows to return (capped).")),
                ("timeout_ms", ToolSchema.Integer("Optional query timeout in milliseconds (capped). Default 10000.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemFeaturesListTool"/> class.
    /// </summary>
    public SystemFeaturesListTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolEnumBinders.TryParseOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "source"),
                SourceByName,
                "source",
                out FeatureInventorySource? sourceOpt,
                out var sourceError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", sourceError ?? "Invalid source value."));
        }
        var source = sourceOpt ?? FeatureInventorySource.Any;

        var nameContains = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var optionalStateRaw = ToolArgs.GetOptionalTrimmed(arguments, "optional_state");
        WindowsOptionalFeatureState? optionalState = null;
        if (!string.IsNullOrWhiteSpace(optionalStateRaw) &&
            !string.Equals(optionalStateRaw, "any", StringComparison.OrdinalIgnoreCase)) {
            if (!ToolEnumBinders.TryParseOptional(
                    optionalStateRaw,
                    OptionalStateByName,
                    "optional_state",
                    out optionalState,
                    out var optionalStateError)) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", optionalStateError ?? "Invalid optional_state value."));
            }
        }

        var max = ResolveBoundedOptionLimit(arguments, "max_entries");
        var timeoutMs = ResolveTimeoutMs(arguments);

        if (!FeatureInventoryQueryExecutor.TryExecute(
                request: new FeatureInventoryQueryRequest {
                    Source = source,
                    NameContains = nameContains,
                    OptionalState = optionalState,
                    MaxResults = max,
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Feature query failed."));
        }

        var result = queryResult ?? new FeatureInventoryQueryResult { Source = source };
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Features,
            viewRowsPath: "features_view",
            title: "Features (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                meta.Add("source", ToolEnumBinders.ToName(source, SourceNames));
                meta.Add("timeout_ms", timeoutMs);
                if (!string.IsNullOrWhiteSpace(nameContains)) {
                    meta.Add("name_contains", nameContains);
                }
                if (optionalState.HasValue) {
                    meta.Add("optional_state", ToolEnumBinders.ToName(optionalState.Value, OptionalStateNames));
                }
            });
        return Task.FromResult(response);
    }
}
