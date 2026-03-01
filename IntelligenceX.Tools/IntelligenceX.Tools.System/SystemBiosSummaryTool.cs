using System;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Bios;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns BIOS and optional baseboard identity summary (read-only).
/// </summary>
public sealed class SystemBiosSummaryTool : SystemToolBase, ITool {
    private sealed record BiosSummaryRequest(
        string? ComputerName,
        string Target,
        bool IncludeBaseBoard,
        int TimeoutMs,
        TimeSpan Timeout);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_bios_summary",
        "Return BIOS version/release/serial and optional baseboard identity summary for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_baseboard", ToolSchema.Boolean("When true (default), include baseboard identity summary.")),
                ("timeout_ms", ToolSchema.Integer("Optional query timeout in milliseconds (capped). Default 8000.")))
            .NoAdditionalProperties());

    private sealed record SystemBiosSummaryResult(
        string ComputerName,
        int TimeoutMs,
        BiosInfo Bios,
        BaseBoardInfo? BaseBoard,
        int? BiosAgeDays);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBiosSummaryTool"/> class.
    /// </summary>
    public SystemBiosSummaryTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<BiosSummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var includeBaseBoard = reader.Boolean("include_baseboard", defaultValue: true);
            var timeoutMs = ResolveTimeoutMs(arguments, defaultValue: 8_000);
            var target = ResolveTargetComputerName(computerName);

            return ToolRequestBindingResult<BiosSummaryRequest>.Success(new BiosSummaryRequest(
                ComputerName: computerName,
                Target: target,
                IncludeBaseBoard: includeBaseBoard,
                TimeoutMs: timeoutMs,
                Timeout: TimeSpan.FromMilliseconds(timeoutMs)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<BiosSummaryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_bios_summary");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;

        try {
            var bios = await Bios.GetAsync(request.ComputerName, request.Timeout, cancellationToken).ConfigureAwait(false);
            BaseBoardInfo? baseBoard = null;
            if (request.IncludeBaseBoard) {
                baseBoard = await BaseBoardInfoQuery.GetAsync(request.Target, request.Timeout, cancellationToken).ConfigureAwait(false);
            }

            int? biosAgeDays = null;
            if (bios.ReleaseDate.HasValue) {
                biosAgeDays = (int)Math.Max(0, (DateTime.UtcNow - bios.ReleaseDate.Value.ToUniversalTime()).TotalDays);
            }

            var model = new SystemBiosSummaryResult(
                ComputerName: request.Target,
                TimeoutMs: request.TimeoutMs,
                Bios: bios,
                BaseBoard: baseBoard,
                BiosAgeDays: biosAgeDays);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "System BIOS summary",
                facts: new[] {
                    ("Computer", request.Target),
                    ("BiosVersion", bios.Version ?? string.Empty),
                    ("BiosSerial", bios.SerialNumber ?? string.Empty),
                    ("BiosReleaseDateUtc", bios.ReleaseDate?.ToUniversalTime().ToString("O") ?? string.Empty),
                    ("BiosAgeDays", biosAgeDays?.ToString() ?? string.Empty),
                    ("BaseBoardManufacturer", baseBoard?.Manufacturer ?? string.Empty),
                    ("BaseBoardProduct", baseBoard?.Product ?? string.Empty)
                },
                meta: BuildFactsMeta(
                    count: 1,
                    truncated: false,
                    target: request.Target,
                    mutate: meta => meta
                        .Add("include_baseboard", request.IncludeBaseBoard)
                        .Add("timeout_ms", request.TimeoutMs)),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "BIOS summary query failed.");
        }
    }
}
