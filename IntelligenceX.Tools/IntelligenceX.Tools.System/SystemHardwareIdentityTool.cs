using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Runtime;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns BIOS and baseboard identity details (read-only).
/// </summary>
public sealed class SystemHardwareIdentityTool : SystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "system_hardware_identity",
        "Return BIOS and baseboard identity details (read-only).",
        ToolSchema.Object(
                ("include_bios", ToolSchema.Boolean("Include BIOS fields. Default true.")),
                ("include_baseboard", ToolSchema.Boolean("Include baseboard fields. Default true.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemHardwareIdentityTool"/> class.
    /// </summary>
    public SystemHardwareIdentityTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var includeBios = arguments?.GetBoolean("include_bios", defaultValue: true) ?? true;
        var includeBaseBoard = arguments?.GetBoolean("include_baseboard", defaultValue: true) ?? true;

        if (!includeBios && !includeBaseBoard) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "At least one of include_bios or include_baseboard must be true."));
        }

        var attempt = SystemRuntimeQueryExecutor.TryExecute(
            request: new SystemRuntimeQueryRequest {
                IncludeOperatingSystemSummary = false,
                IncludeOperatingSystemDetail = false,
                IncludeComputerSystem = false,
                IncludeBios = includeBios,
                IncludeBaseBoard = includeBaseBoard
            },
            cancellationToken: cancellationToken);
        if (!attempt.Success) {
            return Task.FromResult(ErrorFromFailure(attempt.Failure, static x => x.Code, static x => x.Message, defaultMessage: "Hardware identity query failed."));
        }

        var result = attempt.Result!;

        var facts = new List<(string Key, string Value)>();
        if (includeBios && result.Bios is not null) {
            facts.Add(("BIOS Version", result.Bios.Version ?? string.Empty));
            facts.Add(("BIOS Serial", result.Bios.SerialNumber ?? string.Empty));
            facts.Add(("BIOS Release (UTC)", ToolTime.FormatUtc(result.Bios.ReleaseDate)));
        }
        if (includeBaseBoard && result.BaseBoard is not null) {
            facts.Add(("Board Manufacturer", result.BaseBoard.Manufacturer ?? string.Empty));
            facts.Add(("Board Product", result.BaseBoard.Product ?? string.Empty));
            facts.Add(("Board Serial", result.BaseBoard.SerialNumber ?? string.Empty));
        }

        return Task.FromResult(ToolResponse.OkFactsModel(
            model: result,
            title: "Hardware identity",
            facts: facts,
            meta: ToolOutputHints.Meta(count: facts.Count, truncated: false),
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

