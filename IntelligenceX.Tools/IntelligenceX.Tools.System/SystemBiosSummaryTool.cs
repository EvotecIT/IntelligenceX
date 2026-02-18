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
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows()) {
            return ToolResponse.Error("not_supported", "system_bios_summary is available only on Windows hosts.");
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var includeBaseBoard = ToolArgs.GetBoolean(arguments, "include_baseboard", defaultValue: true);
        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", 8000, 200, 120000);
        var target = string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName!;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        try {
            var bios = await Bios.GetAsync(computerName, timeout, cancellationToken).ConfigureAwait(false);
            BaseBoardInfo? baseBoard = null;
            if (includeBaseBoard) {
                baseBoard = await BaseBoardInfoQuery.GetAsync(target, timeout, cancellationToken).ConfigureAwait(false);
            }

            int? biosAgeDays = null;
            if (bios.ReleaseDate.HasValue) {
                biosAgeDays = (int)Math.Max(0, (DateTime.UtcNow - bios.ReleaseDate.Value.ToUniversalTime()).TotalDays);
            }

            var model = new SystemBiosSummaryResult(
                ComputerName: target,
                TimeoutMs: timeoutMs,
                Bios: bios,
                BaseBoard: baseBoard,
                BiosAgeDays: biosAgeDays);

            return ToolResponse.OkFactsModel(
                model: model,
                title: "System BIOS summary",
                facts: new[] {
                    ("Computer", target),
                    ("BiosVersion", bios.Version ?? string.Empty),
                    ("BiosSerial", bios.SerialNumber ?? string.Empty),
                    ("BiosReleaseDateUtc", bios.ReleaseDate?.ToUniversalTime().ToString("O") ?? string.Empty),
                    ("BiosAgeDays", biosAgeDays?.ToString() ?? string.Empty),
                    ("BaseBoardManufacturer", baseBoard?.Manufacturer ?? string.Empty),
                    ("BaseBoardProduct", baseBoard?.Product ?? string.Empty)
                },
                meta: ToolOutputHints.Meta(count: 1, truncated: false)
                    .Add("computer_name", target)
                    .Add("include_baseboard", includeBaseBoard)
                    .Add("timeout_ms", timeoutMs),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ToolResponse.Error("query_failed", $"BIOS summary query failed: {ex.Message}");
        }
    }
}
