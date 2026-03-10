using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Backup;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns backup and recovery posture for the local or remote Windows host.
/// </summary>
public sealed class SystemBackupPostureTool : SystemToolBase, ITool {
    private sealed record BackupPostureRequest(
        string? ComputerName,
        string Target,
        bool IncludeShadowCopies,
        int MaxShadowCopies,
        bool IncludeRestorePoints,
        int MaxRestorePoints);

    private sealed record BackupPostureResponse(
        string ComputerName,
        bool? VssServiceInstalled,
        bool? VssServiceRunning,
        string? VssServiceStartupType,
        bool? SwprvServiceInstalled,
        bool? SwprvServiceRunning,
        string? SwprvServiceStartupType,
        bool? SystemRestorePolicyDisabled,
        bool? SystemRestoreEnabled,
        int ShadowCopyCount,
        DateTime? LatestShadowCopyCreatedUtc,
        int RestorePointCount,
        DateTime? LatestRestorePointCreatedUtc,
        IReadOnlyList<ShadowCopyInfo> ShadowCopies,
        IReadOnlyList<RestorePointInfo> RestorePoints,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_backup_posture",
        "Return backup and recovery posture (VSS/System Restore/shadow copies/restore points) for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_shadow_copies", ToolSchema.Boolean("When true, include capped shadow-copy rows. Default false.")),
                ("max_shadow_copies", ToolSchema.Integer("Optional maximum shadow-copy rows when include_shadow_copies=true (capped). Default 25.")),
                ("include_restore_points", ToolSchema.Boolean("When true, include capped restore-point rows. Default false.")),
                ("max_restore_points", ToolSchema.Integer("Optional maximum restore-point rows when include_restore_points=true (capped). Default 25.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBackupPostureTool"/> class.
    /// </summary>
    public SystemBackupPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<BackupPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<BackupPostureRequest>.Success(new BackupPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeShadowCopies: reader.Boolean("include_shadow_copies", defaultValue: false),
                MaxShadowCopies: ToolArgs.GetCappedInt32(arguments, "max_shadow_copies", 25, 1, 250),
                IncludeRestorePoints: reader.Boolean("include_restore_points", defaultValue: false),
                MaxRestorePoints: ToolArgs.GetCappedInt32(arguments, "max_restore_points", 25, 1, 250)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<BackupPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_backup_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await Backup
                .GetAsync(request.ComputerName, cancellationToken)
                .ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var shadowCopies = request.IncludeShadowCopies
                ? CapRows(posture.ShadowCopies, request.MaxShadowCopies, out _, out _)
                : Array.Empty<ShadowCopyInfo>();
            var restorePoints = request.IncludeRestorePoints
                ? CapRows(posture.RestorePoints, request.MaxRestorePoints, out _, out _)
                : Array.Empty<RestorePointInfo>();
            var warnings = BuildWarnings(posture);
            var model = new BackupPostureResponse(
                ComputerName: effectiveComputerName,
                VssServiceInstalled: posture.VssServiceInstalled,
                VssServiceRunning: posture.VssServiceRunning,
                VssServiceStartupType: posture.VssServiceStartupType,
                SwprvServiceInstalled: posture.SwprvServiceInstalled,
                SwprvServiceRunning: posture.SwprvServiceRunning,
                SwprvServiceStartupType: posture.SwprvServiceStartupType,
                SystemRestorePolicyDisabled: posture.SystemRestorePolicyDisabled,
                SystemRestoreEnabled: posture.SystemRestoreEnabled,
                ShadowCopyCount: posture.ShadowCopyCount,
                LatestShadowCopyCreatedUtc: posture.LatestShadowCopyCreatedUtc,
                RestorePointCount: posture.RestorePointCount,
                LatestRestorePointCreatedUtc: posture.LatestRestorePointCreatedUtc,
                ShadowCopies: shadowCopies,
                RestorePoints: restorePoints,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName, mutate: x => {
                x.Add("include_shadow_copies", request.IncludeShadowCopies);
                x.Add("include_restore_points", request.IncludeRestorePoints);
                if (request.IncludeShadowCopies) {
                    x.Add("max_shadow_copies", request.MaxShadowCopies);
                }
                if (request.IncludeRestorePoints) {
                    x.Add("max_restore_points", request.MaxRestorePoints);
                }
            });
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_backup_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: posture.ShadowCopyCount + posture.RestorePointCount + 2,
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Backup posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("VSS Service Installed", FormatNullableBool(posture.VssServiceInstalled)),
                    ("VSS Service Running", FormatNullableBool(posture.VssServiceRunning)),
                    ("System Restore Enabled", FormatNullableBool(posture.SystemRestoreEnabled)),
                    ("Shadow Copy Count", posture.ShadowCopyCount.ToString(CultureInfo.InvariantCulture)),
                    ("Restore Point Count", posture.RestorePointCount.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Backup posture query failed.");
        }
    }

    private static IReadOnlyList<string> BuildWarnings(BackupPostureInfo posture) {
        var warnings = new List<string>();
        if (posture.VssServiceInstalled == true && posture.VssServiceRunning == false) {
            warnings.Add("VSS service is installed but not running.");
        }
        if (posture.SwprvServiceInstalled == true && posture.SwprvServiceRunning == false) {
            warnings.Add("Software Shadow Copy Provider service is installed but not running.");
        }
        if (posture.SystemRestoreEnabled == false) {
            warnings.Add("System Restore appears disabled.");
        }
        if (posture.ShadowCopyCount == 0) {
            warnings.Add("No shadow copies were discovered.");
        }
        if (posture.RestorePointCount == 0) {
            warnings.Add("No restore points were discovered.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
