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
    private sealed record FeaturesListRequest(
        string? ComputerName,
        string Target,
        FeatureInventorySource Source,
        string? NameContains,
        WindowsOptionalFeatureState? OptionalState,
        int MaxEntries,
        int TimeoutMs);

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
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<FeaturesListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("source"),
                    SourceByName,
                    "source",
                    out FeatureInventorySource? sourceOpt,
                    out var sourceError)) {
                return ToolRequestBindingResult<FeaturesListRequest>.Failure(sourceError ?? "Invalid source value.");
            }
            var source = sourceOpt ?? FeatureInventorySource.Any;

            var optionalStateRaw = reader.OptionalString("optional_state");
            WindowsOptionalFeatureState? optionalState = null;
            if (!string.IsNullOrWhiteSpace(optionalStateRaw) &&
                !string.Equals(optionalStateRaw, "any", StringComparison.OrdinalIgnoreCase)) {
                if (!ToolEnumBinders.TryParseOptional(
                        optionalStateRaw,
                        OptionalStateByName,
                        "optional_state",
                        out optionalState,
                        out var optionalStateError)) {
                    return ToolRequestBindingResult<FeaturesListRequest>.Failure(optionalStateError ?? "Invalid optional_state value.");
                }
            }

            return ToolRequestBindingResult<FeaturesListRequest>.Success(new FeaturesListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                Source: source,
                NameContains: reader.OptionalString("name_contains"),
                OptionalState: optionalState,
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries"),
                TimeoutMs: ResolveTimeoutMs(arguments)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<FeaturesListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!FeatureInventoryQueryExecutor.TryExecute(
                request: new FeatureInventoryQueryRequest {
                    ComputerName = request.ComputerName,
                    Source = request.Source,
                    NameContains = request.NameContains,
                    OptionalState = request.OptionalState,
                    MaxResults = request.MaxEntries,
                    Timeout = TimeSpan.FromMilliseconds(request.TimeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Feature query failed."));
        }

        var result = queryResult ?? new FeatureInventoryQueryResult { Source = request.Source };
        var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Features,
            viewRowsPath: "features_view",
            title: "Features (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, effectiveComputerName);
                AddMaxResultsMeta(meta, request.MaxEntries);
                meta.Add("source", ToolEnumBinders.ToName(request.Source, SourceNames));
                meta.Add("timeout_ms", request.TimeoutMs);
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                if (request.OptionalState.HasValue) {
                    meta.Add("optional_state", ToolEnumBinders.ToName(request.OptionalState.Value, OptionalStateNames));
                }
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_features_list",
                    targetComputer: effectiveComputerName,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: result.Scanned,
                    truncated: result.Truncated);
            });
        return Task.FromResult(response);
    }
}
